using System.IO;
using System.Threading.Tasks;
using CreateMapping.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CreateMapping.Tests;

public class SqlScriptParserTests
{
    [Fact]
    public async Task ParsesAllColumnsFromCaseMigrant()
    {
        var parser = new SqlScriptParser(new NullLogger<SqlScriptParser>());
        var path = Path.Combine("docs","CaseMigrant.sql");
        Assert.True(File.Exists(path), $"Test script not found at {path}");
        var meta = await parser.ParseAsync(path, "dbo.MigrantCase");
        // Expect number of column definitions in script (count manually)
        // MigrantCaseID, CaseGUID, CaseNo, RegistrationDate, SendingMission, PrimarySource, PrimaryRefNo, DestinationCountry, Category, SecondarySource,
        // SecondaryReferenceNo, ReferralDate, ReferralAgency, ReferralEntity, Location, LocationCountry, BasedCity, BasedCountry, PromissoryNoteCategory,
        // FinalDestination, EarliestTravelDate, LatestTravelDate, GlobalCaseStatus, Remarks, ChangeStatusReason, ChangeStatusOtherReason, CreatedBy,
        // ManagingMission, LastDateModified, IsRevoke, ValidFrom, ValidTo, CaseWorker, IsUnaccompaniedMinor, xMFID, FinalDestinationType, ReferralAgencyContact,
        // trigger_timestamp, isExternal, rowguid => total 39
    Assert.Equal(40, meta.Columns.Count);
    }
}
