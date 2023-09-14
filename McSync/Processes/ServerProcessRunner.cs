using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using McSync.Exceptions;
using McSync.Files;
using McSync.Files.Local;
using McSync.Server.Info;
using McSync.Utils;

namespace McSync.Processes
{
    public class ServerProcessRunner
    {
        private readonly FlagSynchronizer _flagSynchronizer;
        private readonly Log _log;
        private readonly ProcessRunner _processRunner;

        public ServerProcessRunner(Log log, ProcessRunner processRunner, FlagSynchronizer flagSynchronizer)
        {
            _log = log;
            _processRunner = processRunner;
            _flagSynchronizer = flagSynchronizer;
        }

        public void RunUntilClosed()
        {
            List<Process> serverProcesses = StartAllProcesses();
            _processRunner.WaitProcessesToBeClosed(serverProcesses);
        }

        private void CheckIfPortableJavaIsPresent()
        {
            DirectoryInfo portableJavaHome = GetPortableJavaHome();
            if (!IsJavaPresentInJavaHome(portableJavaHome)) throw new JreNotFoundException();
        }

        private void ExecuteTokenCreation()
        {
            const string createTokenCommand = "ngrok authtoken 22NyU96RrxqrNvxb5Y7eJw08ZKl_5ZVerYF6Ei5XJN5b9E2TX";
            Process tokenCreator = _processRunner.RunCmdCommand(createTokenCommand);
            tokenCreator.WaitForExit();
        }

        private DirectoryInfo GetPortableJavaHome()
        {
            try
            {
                return new DirectoryInfo(Paths.Java17Home);
            }
            catch (DirectoryNotFoundException)
            {
                _log.Error($"Java 17 home folder is not found: {Paths.Java17Home}");
                Environment.Exit(1);
            }

            return null;
        }

        private bool IsJavaPresentInJavaHome(DirectoryInfo javaHome)
        {
            return javaHome.GetFiles().Any(file => file.Name == "java.exe");
        }

        private List<Process> StartAllProcesses()
        {
            _log.Server(RuntimeStatus.Starting);

            ExecuteTokenCreation();
            List<Process> processes = StartServerProcesses();

            _log.Server(RuntimeStatus.Running);
            _flagSynchronizer.UpdateFlags(PersistedStatus.Running);
            return processes;
        }

        private Process StartServerJar()
        {
            CheckIfPortableJavaIsPresent();
            string serverJar = new DirectoryInfo(Paths.ServerPath)
                .GetFiles()
                .FirstOrDefault(file => file.Extension == ".jar")?.Name;

            string arguments = $@"-Xms4G -Xmx4G -XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 
-XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC -XX:+AlwaysPreTouch -XX:G1HeapWastePercent=5 
-XX:G1MixedGCCountTarget=4 -XX:G1MixedGCLiveThresholdPercent=90 -XX:G1RSetUpdatingPauseTimePercent=5 
-XX:SurvivorRatio=32 -XX:+PerfDisableSharedMem -XX:MaxTenuringThreshold=1 -XX:G1NewSizePercent=30 
-XX:G1MaxNewSizePercent=40 -XX:G1HeapRegionSize=8M -XX:G1ReservePercent=20 -XX:InitiatingHeapOccupancyPercent=15 
-Dusing.aikars.flags=https://mcflags.emc.gs -Daikars.new.flags=true -jar {serverJar} nogui";
            string java17Path = $@"{Paths.Java17Home}\java";
            return _processRunner.RunCmdCommand($"{java17Path} {arguments}");
        }

        private List<Process> StartServerProcesses()
        {
            return new List<Process>
            {
                StartServerJar(),
                StartTcpTunnelProvider()
            };
        }

        private Process StartTcpTunnelProvider()
        {
            string openNgrokCommand = "ngrok.exe tcp 25565 --region=eu";
            return _processRunner.RunCmdCommand(openNgrokCommand);
        }
    }
}