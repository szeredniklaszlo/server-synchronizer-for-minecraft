using System;
using System.Management;
using System.Threading.Tasks;
using Autofac;
using McSync.Files;
using McSync.Files.Local;
using McSync.Files.Local.HashCalculator;
using McSync.Files.Remote;
using McSync.Processes;
using McSync.Server;
using McSync.Utils;

namespace McSync
{
    internal static class Program
    {
        private const string AppName = "Minecraft Server Sync";
        public static readonly ParallelOptions ParallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 24 };

        private static IContainer _container;
        private static ServerRunner _serverRunner;
        private static Log _log;

        public static void UpdateConsoleTitleWithNetworkTraffic()
        {
            var remoteFileManager = _container.Resolve<RemoteFileManager>();
            Console.Title =
                $"McSync | ↓ / ↑: {remoteFileManager.DownloadedMegabytes} / {remoteFileManager.UploadedMegabytes} MB";
        }

        private static void InitializeIoCContainer()
        {
            var builder = new ContainerBuilder();
            builder.RegisterType<Log>();
            builder.RegisterType<PathUtils>();
            builder.RegisterType<EnumerableUtils>();

            builder.Register(container =>
            {
                var log = container.Resolve<Log>();
                return new GDriveServiceFactory(AppName, Paths.Credentials, log);
            }).SingleInstance();
            builder.RegisterType<GDriveServicePool>().SingleInstance();
            builder.RegisterType<GDriveServiceRetrier>().SingleInstance();

            builder.RegisterType<LocalFileManager>().SingleInstance();
            builder.RegisterType<HashCalculatorFactory>().SingleInstance();
            builder.RegisterType<RemoteFileManager>().SingleInstance();
            builder.RegisterType<FileSynchronizer>().SingleInstance();

            builder.RegisterType<ManagementObjectSearcher>();
            builder.RegisterType<HardwareInfoRetriever>();

            builder.RegisterType<RuntimeStatusUpdater>().SingleInstance();
            builder.RegisterType<ProcessRunner>().SingleInstance();
            builder.RegisterType<ServerProcessRunner>().SingleInstance();
            builder.RegisterType<FlagSynchronizer>().SingleInstance();
            builder.RegisterType<LocalServerUpdater>().SingleInstance();
            builder.RegisterType<RemoteServerUpdater>().SingleInstance();
            builder.RegisterType<ServerRunner>().SingleInstance();

            _container = builder.Build();
        }

        private static void Main()
        {
            try
            {
                InitializeIoCContainer();
                ResolveProgramDependencies();
                _serverRunner.Execute();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

            _log.Info("Done! Press enter to exit...");
            Console.ReadLine();
        }

        private static void ResolveProgramDependencies()
        {
            _serverRunner = _container.Resolve<ServerRunner>();
            _log = _container.Resolve<Log>();
        }
    }
}