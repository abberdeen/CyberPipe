using CyberPipe.Contracts;

// Host implementation: TASK-020. Configuration and startup validation: TASK-003.
try
{
    _ = CyberPipeConfigurationLoader.Load();
}
catch (ConfigurationValidationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

return 0;
