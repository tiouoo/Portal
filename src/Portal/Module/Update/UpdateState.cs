namespace Portal.Module.Update;

public enum UpdateState
{
    Idle,
    Checking,
    Latest,
    DownloadingDelta,
    ReadyToRestart,
    ManualDownloadRequired
}
