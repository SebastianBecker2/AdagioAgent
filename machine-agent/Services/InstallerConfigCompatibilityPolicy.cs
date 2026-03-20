namespace AdagioMachineAgent.Services;

/// <summary>
/// Validates installer-authored configuration compatibility against the
/// schema range supported by the running agent.
/// </summary>
public static class InstallerConfigCompatibilityPolicy
{
    public static void Validate(InstallerConfigOptions installerConfigOptions)
    {
        if (installerConfigOptions.SchemaVersion < InstallerConfigOptions.MinSupportedSchemaVersion ||
            installerConfigOptions.SchemaVersion > InstallerConfigOptions.MaxSupportedSchemaVersion)
        {
            throw new InvalidOperationException(
                $"InstallerConfig.SchemaVersion '{installerConfigOptions.SchemaVersion}' is not supported. " +
                $"Supported range: {InstallerConfigOptions.MinSupportedSchemaVersion}-{InstallerConfigOptions.MaxSupportedSchemaVersion}.");
        }
    }
}