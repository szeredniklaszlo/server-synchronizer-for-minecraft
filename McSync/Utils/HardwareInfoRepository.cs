using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;

namespace McSync.Utils
{
    public class HardwareInfoRepository
    {
        private readonly ManagementObjectSearcher _managementObjectSearcher = new ManagementObjectSearcher();

        public string GetPcId()
        {
            var identities = new List<string>()
            {
                Environment.MachineName,
                GetMotherboardSerial(),
                GetProcessorId()
            };

            var notEmptyIdentities = identities.Where(item => item != string.Empty);
            return string.Join(".", notEmptyIdentities);
        }

        public string GetMotherboardSerial()
        {
            return GetProperty("Win32_BaseBoard", "SerialNumber");
        }
        
        public string GetProcessorId()
        {
            return GetProperty("Win32_Processor", "ProcessorId");
        }

        private string GetProperty(string selectQuery, string propertyKey)
        {
            _managementObjectSearcher.Query = new SelectQuery(selectQuery);

            var property = SearchPropertyByKey(propertyKey);
            return TrimLeadingPeriod(property);
        }

        private static string TrimLeadingPeriod(string property)
        {
            return property == string.Empty ? string.Empty : property.Substring(1);
        }

        private string SearchPropertyByKey(string property)
        {
            try
            {
                var searchResultCollection = _managementObjectSearcher.Get();
                return AppendPropertiesOfSearchResultCollection(searchResultCollection, property);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string AppendPropertiesOfSearchResultCollection(ManagementObjectCollection searchResultCollection, string property)
        {
            var resultBuilder = new StringBuilder();
            
            foreach (ManagementBaseObject item in searchResultCollection)
            {
                string processorId = item.Properties[property].Value.ToString();
                resultBuilder.Append(".").Append(processorId);
            }
            
            return resultBuilder.ToString();
        }
    }
}