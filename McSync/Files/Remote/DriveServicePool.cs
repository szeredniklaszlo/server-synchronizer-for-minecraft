using System;
using System.Collections.Concurrent;
using Google.Apis.Drive.v3;
using McSync.Utils;

namespace McSync.Files.Remote
{
    public class DriveServicePool
    {
        private readonly ConcurrentQueue<DriveService> _availableServices = new ConcurrentQueue<DriveService>();

        private readonly DriveServiceFactory _driveServiceFactory;

        // TODO: write logs
        private readonly Log _log;

        public DriveServicePool(DriveServiceFactory driveServiceFactory, Log log)
        {
            _driveServiceFactory = driveServiceFactory;
            _log = log;
        }

        public void ExecuteWithDriveService(Action<DriveService> action)
        {
            ExecuteWithDriveService(driveService =>
            {
                action(driveService);
                return true;
            });
        }

        public T ExecuteWithDriveService<T>(Func<DriveService, T> func)
        {
            DriveService service = AcquireDriveService();
            try
            {
                T result = func(service);
                ReleaseDriveService(service);
                return result;
            }
            catch
            {
                service.Dispose();
                throw;
            }
        }

        private DriveService AcquireDriveService()
        {
            bool isServiceAvailable = _availableServices.TryDequeue(out DriveService service);
            return isServiceAvailable ? service : CreateNewDriveService();
        }

        private DriveService CreateNewDriveService()
        {
            return _driveServiceFactory.CreateDriveService();
        }

        private void ReleaseDriveService(DriveService service)
        {
            _availableServices.Enqueue(service);
        }
    }
}