using System.Collections.Generic;
using CreateMapping.Models;
using CreateMapping.Services;
using Xunit;

namespace CreateMapping.Tests;

public class SystemFieldClassifierTests
{
    private readonly ISystemFieldClassifier _classifier = new SystemFieldClassifier();

    [Theory]
    [InlineData("createdon", true, SystemFieldType.CreatedOn)]
    [InlineData("createdby", true, SystemFieldType.CreatedBy)]
    [InlineData("modifiedon", true, SystemFieldType.ModifiedOn)]
    [InlineData("modifiedby", true, SystemFieldType.ModifiedBy)]
    [InlineData("ownerid", true, SystemFieldType.Owner)]
    [InlineData("statecode", true, SystemFieldType.State)]
    [InlineData("statuscode", true, SystemFieldType.Status)]
    [InlineData("versionnumber", true, SystemFieldType.Version)]
    [InlineData("customfield", false, SystemFieldType.None)]
    [InlineData("businessname", false, SystemFieldType.None)]
    public void ClassifiesSystemFieldsCorrectly(string logicalName, bool expectedIsSystem, SystemFieldType expectedType)
    {
        var (isSystem, systemType) = _classifier.ClassifyField(logicalName);
        
        Assert.Equal(expectedIsSystem, isSystem);
        Assert.Equal(expectedType, systemType);
    }

    [Theory]
    [InlineData("msft_customfield", true, SystemFieldType.Other)]
    [InlineData("msdyn_businessfield", true, SystemFieldType.Other)]
    [InlineData("mspcat_catalogfield", true, SystemFieldType.Other)]
    public void ClassifiesSystemPrefixFieldsCorrectly(string logicalName, bool expectedIsSystem, SystemFieldType expectedType)
    {
        var (isSystem, systemType) = _classifier.ClassifyField(logicalName);
        
        Assert.Equal(expectedIsSystem, isSystem);
        Assert.Equal(expectedType, systemType);
    }

    [Fact]
    public void CustomFieldsGetHighestPriority()
    {
        var customField = new ColumnMetadata("customfield", "string", true, 100, null, null, IsSystemField: false);
        var systemField = new ColumnMetadata("createdon", "datetime", false, null, null, null, IsSystemField: true, SystemFieldType: SystemFieldType.CreatedOn);
        
        var customPriority = _classifier.GetMappingPriority(customField);
        var systemPriority = _classifier.GetMappingPriority(systemField);
        
        Assert.True(customPriority < systemPriority, "Custom fields should have lower priority numbers (higher priority)");
        Assert.Equal(1, customPriority);
        Assert.Equal(2, systemPriority);
    }

    [Fact]
    public void SystemFieldsPriorityOrderedByImportance()
    {
        var createdOn = new ColumnMetadata("createdon", "datetime", false, null, null, null, IsSystemField: true, SystemFieldType: SystemFieldType.CreatedOn);
        var version = new ColumnMetadata("versionnumber", "bigint", false, null, null, null, IsSystemField: true, SystemFieldType: SystemFieldType.Version);
        var other = new ColumnMetadata("msft_other", "string", true, null, null, null, IsSystemField: true, SystemFieldType: SystemFieldType.Other);
        
        var createdOnPriority = _classifier.GetMappingPriority(createdOn);
        var versionPriority = _classifier.GetMappingPriority(version);
        var otherPriority = _classifier.GetMappingPriority(other);
        
        Assert.True(createdOnPriority < versionPriority, "CreatedOn should have higher priority than Version");
        Assert.True(versionPriority < otherPriority, "Version should have higher priority than Other system fields");
    }
}