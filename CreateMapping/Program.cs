using System.CommandLine;
using CreateMapping.Mapping;
using CreateMapping.Export;
using CreateMapping.Models;
using CreateMapping.Services;
using CreateMapping.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

public class Program
{
	// Entry point (non-async) for easier debugger attachment / breakpoint setting
	public static void Main(string[] args)
	{
		// Allow optional early wait for debugger: detect --debug-wait or env var
		if (args.Contains("--debug-wait", StringComparer.OrdinalIgnoreCase) ||
			string.Equals(Environment.GetEnvironmentVariable("CM_DEBUG_WAIT"), "true", StringComparison.OrdinalIgnoreCase))
		{
			Console.WriteLine("[CreateMapping] Waiting for debugger to attach (process id: " + Environment.ProcessId + ")...");
			while (!Debugger.IsAttached)
			{
				Thread.Sleep(250);
			}
			Debugger.Break();
		}

		// Strip internal diagnostic flag before System.CommandLine parses
		args = args.Where(a => !string.Equals(a, "--debug-wait", StringComparison.OrdinalIgnoreCase)).ToArray();

		try
		{
			var exitCode = RunAsync(args).GetAwaiter().GetResult();
			Environment.Exit(exitCode);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine("FATAL: " + ex);
			Environment.Exit(1);
		}
	}

	private static RootCommand BuildRoot()
	{
		var root = new RootCommand("SQL -> Dataverse mapping generator");
		var sqlTableArg = new Argument<string>(name: "sql-table", description: "SQL table name (optionally schema.table)");
		var dvTableArg = new Argument<string>(name: "dataverse-table", description: "Dataverse logical table name");
		var outputOpt = new Option<string>(name: "--output", () => "output", description: "Output directory");
	var sqlScriptOpt = new Option<string?>(name: "--sql-script", description: "Path to CREATE TABLE script (bypass live SQL)");
	var checkDataverseOpt = new Option<bool>(name: "--check-dataverse", description: "Only test connectivity and retrieve Dataverse entity metadata; no mapping output");
	var checkSqlOpt = new Option<bool>(name: "--check-sql", description: "Only test connectivity and retrieve SQL table metadata; no mapping output");
		root.AddArgument(sqlTableArg);
		root.AddArgument(dvTableArg);
		root.AddOption(outputOpt);
	root.AddOption(sqlScriptOpt);
	root.AddOption(checkDataverseOpt);
	root.AddOption(checkSqlOpt);

	root.SetHandler<string, string, string, string?, bool, bool>(async (sqlTable, dvTable, outputDir, sqlScriptPath, checkDv, checkSql) =>
		{
			var (provider, logger) = BuildServices();
			using (provider)
			{
				try
				{
					string? schema = null;
					string tableOnly = sqlTable;
					if (sqlTable.Contains('.'))
					{
						var parts = sqlTable.Split('.', 2);
						schema = parts[0];
						tableOnly = parts[1];
					}

					var dv = provider.GetRequiredService<IDataverseMetadataProvider>();
					var orchestrator = provider.GetRequiredService<IMappingOrchestrator>();
					var csv = provider.GetRequiredService<ICsvMappingExporter>();
					var json = provider.GetRequiredService<IJsonMappingExporter>();

					TableMetadata sqlMeta;
					if (!string.IsNullOrWhiteSpace(sqlScriptPath))
					{
						var parser = provider.GetRequiredService<ISqlScriptParser>();
						logger.LogInformation("Parsing SQL metadata from script {Script}", sqlScriptPath);
						sqlMeta = await parser.ParseAsync(sqlScriptPath, sqlTable);
					}
					else
					{
						var sql = provider.GetRequiredService<ISqlIntrospector>();
						logger.LogInformation("Retrieving SQL metadata for {Table}", sqlTable);
						sqlMeta = await sql.GetTableMetadataAsync(tableOnly, schema);
					}
					logger.LogInformation("Retrieving Dataverse metadata for {Table}", dvTable);
					var dvMeta = await dv.GetTableMetadataAsync(dvTable);

					if (checkDv || checkSql)
					{
						if (checkSql)
						{
							logger.LogInformation("SQL connectivity OK. Table: {Table} | Columns: {Count}", sqlTable, sqlMeta.Columns.Count);
							foreach (var col in sqlMeta.Columns.Take(25))
							{
								logger.LogInformation(" - {Name} ({Type})", col.Name, col.DataType);
							}
							if (sqlMeta.Columns.Count > 25)
							{
								logger.LogInformation(" ... ({Extra} more columns omitted)", sqlMeta.Columns.Count - 25);
							}
						}
						if (checkDv)
						{
							logger.LogInformation("Dataverse connectivity OK. Entity: {Entity} | Columns: {Count} (Custom: {Custom} System: {System})", 
								dvTable, dvMeta.Columns.Count, dvMeta.Columns.Count(c => !c.IsSystemField), dvMeta.Columns.Count(c => c.IsSystemField));
							foreach (var col in dvMeta.Columns.Take(25))
							{
								logger.LogInformation(" - {Name} ({Type}){SystemFlag}", col.Name, col.DataType, col.IsSystemField ? " [system]" : string.Empty);
							}
							if (dvMeta.Columns.Count > 25)
							{
								logger.LogInformation(" ... ({Extra} more columns omitted)", dvMeta.Columns.Count - 25);
							}
						}
						return; // skip mapping generation
					}

					var weights = WeightsConfig.Default; // future: load from config
					var mapping = await orchestrator.GenerateAsync(sqlMeta, dvMeta, weights);

					Directory.CreateDirectory(outputDir);
					var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
					var baseName = $"mapping_{sqlTable.Replace('.', '_')}_{dvTable}_{timestamp}";
					var csvPath = Path.Combine(outputDir, baseName + ".csv");
					var jsonPath = Path.Combine(outputDir, baseName + ".json");

					await csv.WriteAsync(mapping, csvPath);
					await json.WriteAsync(mapping, jsonPath);

					logger.LogInformation("Mapping files written: {Csv} | {Json}", csvPath, jsonPath);
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Failed to generate mapping");
					Environment.ExitCode = 1;
				}
			}
	}, sqlTableArg, dvTableArg, outputOpt, sqlScriptOpt, checkDataverseOpt, checkSqlOpt);

		return root;
	}

	private static (ServiceProvider provider, ILogger logger) BuildServices()
	{
		var config = new ConfigurationBuilder()
			.AddJsonFile("appsettings.json", optional: true)
			.AddEnvironmentVariables()
			.Build();

		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(config);
		services.AddLogging(b => b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }).SetMinimumLevel(LogLevel.Information));
		services.AddSingleton<ISqlIntrospector, SqlIntrospector>();
		services.AddSingleton<ISqlScriptParser, SqlScriptParser>();
		services.AddSingleton<ISystemFieldClassifier, SystemFieldClassifier>();
		services.AddSingleton<IDataverseMetadataProvider, DataverseMetadataProvider>();
		services.AddHttpClient("azure-openai");
		var aiEnabled = config.GetValue("Ai:Enabled", true);
		if (aiEnabled && !string.IsNullOrWhiteSpace(config["Ai:Endpoint"]) && !string.IsNullOrWhiteSpace(config["Ai:ApiKey"]))
		{
			services.AddSingleton<IAiMapper, AzureOpenAiMapper>();
		}
		else
		{
			services.AddSingleton<IAiMapper, NoOpAiMapper>();
		}
		services.AddSingleton<IMappingOrchestrator, MappingOrchestrator>();
		services.AddSingleton<ICsvMappingExporter, CsvMappingExporter>();
		services.AddSingleton<IJsonMappingExporter, JsonMappingExporter>();

		var provider = services.BuildServiceProvider();
		var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Main");
		return (provider, logger);
	}

	private static async Task<int> RunAsync(string[] args)
	{
		var root = BuildRoot();
		return await root.InvokeAsync(args);
	}
}
