using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;

namespace McSync.Utils
{
    public class HardwareInfoRetriever
    {
        // TODO: write logs
        private readonly Log _log;
        private readonly ManagementObjectSearcher _managementObjectSearcher;

        private ManagementObjectCollection _propertiesOfHardware;

        public HardwareInfoRetriever(Log log, ManagementObjectSearcher managementObjectSearcher)
        {
            _log = log;
            _managementObjectSearcher = managementObjectSearcher;
        }

        public string GetPcId()
        {
            var ids = new List<string>
            {
                Environment.MachineName,
                GetMotherboardSerial(),
                GetProcessorId()
            };

            IEnumerable<string> notEmptyIds = ids.Where(item => item != string.Empty);
            return string.Join(".", notEmptyIds);
        }

        private string ConcatPropertyFromPropertiesOfHardware(string property)
        {
            var resultBuilder = new StringBuilder();

            foreach (ManagementBaseObject item in _propertiesOfHardware)
            {
                string foundProperty = item.Properties[property].Value.ToString();
                resultBuilder.Append("_").Append(foundProperty);
            }

            return resultBuilder.ToString();
        }

        private string GetMotherboardSerial()
        {
            return GetPropertyOfHardware("SerialNumber", "Win32_BaseBoard");
        }

        private string GetProcessorId()
        {
            return GetPropertyOfHardware("ProcessorId", "Win32_Processor");
        }

        private string GetProperty(string property)
        {
            try
            {
                _propertiesOfHardware = _managementObjectSearcher.Get();
                return ConcatPropertyFromPropertiesOfHardware(property);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private string GetPropertyOfHardware(string propertyKey, string hardwareQuery)
        {
            _managementObjectSearcher.Query = new SelectQuery(hardwareQuery);

            string property = GetProperty(propertyKey);
            return TrimLeadingDot(property);
        }

        private string TrimLeadingDot(string str)
        {
            return str == string.Empty ? string.Empty : str.Substring(1);
        }
    }
}