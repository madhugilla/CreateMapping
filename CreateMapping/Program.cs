using System.CommandLine;
using CreateMapping.Mapping;
using CreateMapping.Export;
using CreateMapping.Models;
using CreateMapping.Services;
using CreateMapping.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var root = new RootCommand("SQL -> Dataverse mapping generator");
var sqlTableArg = new Argument<string>(name: "sql-table", description: "SQL table name (optionally schema.table)");
var dvTableArg = new Argument<string>(name: "dataverse-table", description: "Dataverse logical table name");
var outputOpt = new Option<string>(name: "--output", () => "output", description: "Output directory");
var sqlScriptOpt = new Option<string?>(name: "--sql-script", description: "Path to CREATE TABLE script (bypass live SQL)");
root.AddArgument(sqlTableArg);
root.AddArgument(dvTableArg);
root.AddOption(outputOpt);
root.AddOption(sqlScriptOpt);

root.SetHandler(async (sqlTable, dvTable, outputDir, sqlScriptPath) =>
{
	// Configuration
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
	// MappingEngine removed (AI-only mode per user request)
	services.AddHttpClient("azure-openai");
	// Conditional AI registration
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

	using var provider = services.BuildServiceProvider();
	var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Main");

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
}, sqlTableArg, dvTableArg, outputOpt, sqlScriptOpt);

return await root.InvokeAsync(args);
