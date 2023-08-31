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
        public static readonly ParallelOptions ParallelOptions = new ParallelOptions {MaxDegreeOfParallelism = 24};

        private static IContainer _container;
        private static ServerManager _serverManager;
        private static Log _log;

        public static void UpdateConsoleTitleWithNetworkTraffic()
        {
            var remoteFileManager = _container.Resolve<RemoteFileManager>();
            Console.Title =
                $"Minecraft Synchronizer | Downloaded / Uploaded: {remoteFileManager.DownloadedMegabytes} / {remoteFileManager.UploadedMegabytes} MB";
        }

        private static void InitializeIoCContainer()
        {
            var builder = new ContainerBuilder();
            builder.RegisterType<Log>();

            builder.Register(container =>
            {
                var log = container.Resolve<Log>();
                return new DriveServiceFactory(AppName, Paths.Credentials, log);
            }).SingleInstance();
            builder.RegisterType<DriveServicePool>().SingleInstance();

            builder.RegisterType<Retrier>().SingleInstance();

            builder.RegisterType<LocalFileManager>().SingleInstance();
            builder.RegisterType<HashCalculatorFactory>();
            builder.RegisterType<RemoteFileManager>().SingleInstance();
            builder.RegisterType<FileSynchronizer>().SingleInstance();

            builder.RegisterType<ManagementObjectSearcher>();
            builder.RegisterType<HardwareInfoRetriever>();

            builder.RegisterType<ProcessController>().SingleInstance();
            builder.RegisterType<ServerManager>().SingleInstance();

            _container = builder.Build();
        }

        private static void Main()
        {
            InitializeIoCContainer();
            ResolveProgramDependencies();

            _serverManager.Run();
            _log.Info("Done");
        }

        private static void ResolveProgramDependencies()
        {
            _serverManager = _container.Resolve<ServerManager>();
            _log = _container.Resolve<Log>();
        }
    }
}