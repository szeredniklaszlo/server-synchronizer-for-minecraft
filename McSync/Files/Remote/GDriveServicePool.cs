using System;
using System.Collections.Concurrent;
using Google.Apis.Drive.v3;
using McSync.Utils;

namespace McSync.Files.Remote
{
    public class GDriveServicePool
    {
        private readonly ConcurrentQueue<DriveServiceProxy> _availableServices =
            new ConcurrentQueue<DriveServiceProxy>();

        private readonly GDriveServiceFactory _gDriveServiceFactory;

        // TODO: write logs
        private readonly Log _log;

        public GDriveServicePool(GDriveServiceFactory gDriveServiceFactory, Log log)
        {
            _gDriveServiceFactory = gDriveServiceFactory;
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
            var service = AcquireDriveService();
            try
            {
                var result = func(service.DriveService);
                ReleaseDriveService(service);
                return result;
            }
            catch
            {
                service.Dispose();
                throw;
            }
        }

        private DriveServiceProxy AcquireDriveService()
        {
            while (_availableServices.TryDequeue(out var serviceProxy))
                if (serviceProxy.IsAlive)
                    return serviceProxy;

            return CreateNewDriveServiceProxy();
        }

        private DriveServiceProxy CreateNewDriveServiceProxy()
        {
            var driveService = _gDriveServiceFactory.CreateDriveService();
            return new DriveServiceProxy(driveService);
        }

        private void ReleaseDriveService(DriveServiceProxy serviceProxy)
        {
            if (serviceProxy.IsAlive) _availableServices.Enqueue(serviceProxy);
        }
    }
}