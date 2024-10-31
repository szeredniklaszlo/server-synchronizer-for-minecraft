using System;
using System.Threading;
using Google.Apis.Drive.v3;

namespace McSync.Files.Remote
{
    public class DriveServiceProxy : IDisposable
    {
        private readonly Timer _timer;

        public DriveServiceProxy(DriveService driveService, int lifetimeInSeconds = 3500)
        {
            DriveService = driveService;
            IsAlive = true;
            _timer = new Timer(OnTimerElapsed, null, lifetimeInSeconds * 1000, Timeout.Infinite);
        }

        public bool IsAlive { get; private set; }

        public DriveService DriveService { get; }

        public void Dispose()
        {
            if (IsAlive)
            {
                IsAlive = false;
                _timer.Dispose();
                DriveService.Dispose();
            }
        }

        private void OnTimerElapsed(object state)
        {
            Dispose();
        }
    }
}