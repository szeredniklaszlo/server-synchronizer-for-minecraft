using System.Collections.Generic;
using System.Diagnostics;
using McSync.Files.Local;
using McSync.Server.Info;
using McSync.Utils;

namespace McSync.Processes
{
    public class ProcessRunner
    {
        private readonly Log _log;

        public ProcessRunner(Log log)
        {
            _log = log;
        }

        public Process RunCmdCommand(string command)
        {
            command = $"/C {command}";

            var cmd = new Process();
            cmd.StartInfo = new ProcessStartInfo("cmd.exe", command)
            {
                UseShellExecute = true,
                WorkingDirectory = Paths.ServerPath
            };

            cmd.Start();
            return cmd;
        }

        public void WaitProcessesToBeClosed(List<Process> processes)
        {
            if (processes == null)
                return;

            foreach (Process process in processes) process.WaitForExit();

            _log.Server(CalculatedStatus.Stopped);
        }
    }
}