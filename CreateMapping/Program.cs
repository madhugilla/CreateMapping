using System.CommandLine;
using CreateMapping.AI;
using CreateMapping.Services;
using CreateMapping.Mapping;
using CreateMapping.Export;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using CreateMapping;

public class Program
{
	public static void Main(string[] args)
	{
		if (args.Contains("--debug-wait", StringComparer.OrdinalIgnoreCase) ||
			string.Equals(Environment.GetEnvironmentVariable("CM_DEBUG_WAIT"), "true", StringComparison.OrdinalIgnoreCase))
		{
			Console.WriteLine("[CreateMapping] Waiting for debugger to attach (process id: " + Environment.ProcessId + ")...");
			while (!Debugger.IsAttached) Thread.Sleep(250);
			Debugger.Break();
		}

		args = args.Where(a => !string.Equals(a, "--debug-wait", StringComparison.OrdinalIgnoreCase)).ToArray();
		var exit = RunAsync(args).GetAwaiter().GetResult();
		Environment.Exit(exit);
	}

	private static RootCommand BuildRoot(IServiceProvider provider)
	{
		var root = new RootCommand("SQL -> Dataverse mapping generator");
		var sqlTableArg = new Argument<string>("sql-table", description: "SQL table name (optionally schema.table)");
		var dvTableArg = new Argument<string>("dataverse-table", description: "Dataverse logical table name");
		var outputOpt = new Option<string>("--output", () => "output", "Output directory");
		var sqlScriptOpt = new Option<string?>("--sql-script", description: "Path to CREATE TABLE script (bypass live SQL)");
		var checkDataverseOpt = new Option<bool>("--check-dataverse", description: "Only test connectivity and retrieve Dataverse entity metadata; no mapping output");
		var checkSqlOpt = new Option<bool>("--check-sql", description: "Only test connectivity and retrieve SQL table metadata; no mapping output");
		root.AddArgument(sqlTableArg);
		root.AddArgument(dvTableArg);
		root.AddOption(outputOpt);
		root.AddOption(sqlScriptOpt);
		root.AddOption(checkDataverseOpt);
		root.AddOption(checkSqlOpt);

		root.SetHandler<string, string, string, string?, bool, bool>(async (sqlTable, dvTable, outputDir, script, checkDv, checkSql) =>
		{
			var app = provider.GetRequiredService<MappingApp>();
			try
			{
				await app.RunAsync(sqlTable, dvTable, outputDir, script, checkDv, checkSql);
			}
			catch (Exception ex)
			{
				provider.GetRequiredService<ILoggerFactory>().CreateLogger("Main").LogError(ex, "Execution failed");
				Environment.ExitCode = 1;
			}
		}, sqlTableArg, dvTableArg, outputOpt, sqlScriptOpt, checkDataverseOpt, checkSqlOpt);
		return root;
	}

	private static IServiceProvider BuildServices()
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
			services.AddSingleton<IAiMapper, AzureOpenAiMapper>();
		else
			services.AddSingleton<IAiMapper, NoOpAiMapper>();
		services.AddSingleton<IMappingOrchestrator, MappingOrchestrator>();
		services.AddSingleton<ICsvMappingExporter, CsvMappingExporter>();
		services.AddSingleton<IJsonMappingExporter, JsonMappingExporter>();
		services.AddSingleton<MappingApp>();
		return services.BuildServiceProvider();
	}

	private static async Task<int> RunAsync(string[] args)
	{
		var provider = BuildServices();
		try
		{
			var root = BuildRoot(provider);
			return await root.InvokeAsync(args);
		}
		finally
		{
			if (provider is IDisposable d) d.Dispose();
		}
	}
}
