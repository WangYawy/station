namespace Station.Application.Storage;
public interface IStorageConfiguration
{
    string DirectoryTemplate { get; set; }
    string StationNo { get; set; }
    int RetryCount { get; set; }
    int RetryIntervalSeconds { get; set; }
    bool VerifyRemoteSm3 { get; set; }
}
