using System;
using System.IO;
using System.Net;
using NewRelic.Platform.Sdk.Configuration;
using NewRelic.Platform.Sdk.Utils;

namespace NewRelic.Platform.Sdk.Binding
{
    public class Context : IContext
    {
        private RequestData _requestData;
        private string _licenseKey;
        private readonly INewRelicConfig _newRelicConfig;
        internal string ServiceUri
        {
            get
            {
                return this._newRelicConfig.Endpoint;
            }
        }

        internal string LicenseKey
        {
            get
            {
                return _licenseKey ?? this._newRelicConfig.LicenseKey;
            }
        }

        public string Version
        {
            get
            {
                return _requestData.Version;
            }
            set
            {
                _requestData.Version = value;
            }
        }

        // Exposed to enable functional testing for plugin developers
        public RequestData RequestData
        {
            get
            {
                return _requestData;
            }
        }

        /// <summary>
        /// This class is responsible for maintaining metrics that have been reported as well as sending them to the New Relic 
        /// service. Any Components that share a Request reference will have their data sent to the service in one request.
        /// </summary>
        /// <param name="config">The configuration that should be used.</param>
        public Context(INewRelicConfig config = null)
            : this(null, config)
        {
        }

        /// <summary>
        /// This class is responsible for maintaining metrics that have been reported as well as sending them to the New Relic 
        /// service. Any Components that share a Request reference will have their data sent to the service in one request.
        /// </summary>
        /// <param name="licenseKey">The New Relic license key.</param>
        /// <param name="config">The configuration that should be used.</param>
        public Context(string licenseKey, INewRelicConfig config = null)
        {
            this._newRelicConfig = config ?? NewRelicConfig.Instance;

            this._licenseKey = licenseKey;
            this._requestData = new RequestData();

        }

        /// <summary>
        /// Prepare a New Relic metric to be delivered to the service at the end of this poll cycle.  
        /// </summary>
        /// <param name="guid">A string guid identifying the plugin this is associated with (e.g. 'com.yourcompany.pluginname')</param>
        /// <param name="componentName">A string name identifying the plugin agent that will appear in the service UI (e.g. 'MyPlugin')</param>
        /// <param name="metricName">A string name representing the name of the metric (e.g. 'Category/Name')</param>
        /// <param name="units">The units of the metric you are sending to the service (e.g. 'byte/second')</param>
        /// <param name="value">The non-negative float value representing this value</param>
        public void ReportMetric(string guid, string componentName, string metricName, string units, float? value)
        {
            if (string.IsNullOrEmpty(guid))
            {
                throw new ArgumentNullException("guid", "Null parameter was passed to ReportMetric()");
            }

            if (string.IsNullOrEmpty(componentName))
            {
                throw new ArgumentNullException("componentName", "Null parameter was passed to ReportMetric()");
            }

            if (string.IsNullOrEmpty(metricName))
            {
                throw new ArgumentNullException("metricName", "Null parameter was passed to ReportMetric()");
            }

            if (string.IsNullOrEmpty(units))
            {
                throw new ArgumentNullException("units", "Null parameter was passed to ReportMetric()");
            }

            // Ensure the value is not null since EpochProcessors will return 0 on initial processing
            if (value.HasValue)
            {
                if (value.Value < 0)
                {
                    throw new ArgumentException("New Relic Platform does not currently support negative values", "value");
                }

                _requestData.AddMetric(guid, componentName, metricName, units, value.Value);
            }
        }

        /// <summary>
        /// Prepare a New Relic metric to be delivered to the service at the end of this poll cycle.
        /// </summary>
        /// <param name="guid">A string guid identifying the plugin this is associated with (e.g. 'com.yourcompany.pluginname')</param>
        /// <param name="componentName">A string name identifying the plugin agent that will appear in the service UI (e.g. 'MyPlugin')</param>
        /// <param name="metricName">A string name representing the name of the metric (e.g. 'Category/Name')</param>
        /// <param name="units">The units of the metric you are sending to the service (e.g. 'byte/second')</param>
        /// <param name="value">The non-negative float value representing this value</param>
        /// <param name="count">The int value representing how many poll cycle this metric has been aggregated for</param>
        /// <param name="min">The non-negative float value representing this min value for this poll cycle</param>
        /// <param name="max">The non-negative float value representing this max value for this poll cycle</param>
        /// <param name="sumOfSquares">The non-negative float value representing the sum of square values for this poll cycle</param>
        public void ReportMetric(string guid, string componentName, string metricName, string units, float value, int count, float min, float max, float sumOfSquares)
        {
            if (string.IsNullOrEmpty(guid))
            {
                throw new ArgumentNullException("guid", "Null parameter was passed to ReportMetric()");
            }

            if (string.IsNullOrEmpty(componentName))
            {
                throw new ArgumentNullException("componentName", "Null parameter was passed to ReportMetric()");
            }

            if (string.IsNullOrEmpty(metricName))
            {
                throw new ArgumentNullException("metricName", "Null parameter was passed to ReportMetric()");
            }

            if (string.IsNullOrEmpty(units))
            {
                throw new ArgumentNullException("units", "Null parameter was passed to ReportMetric()");
            }

            if (count < 1)
            {
                throw new ArgumentException("New Relic Platform does not support count values less than 1", "count");
            }

            // Ensure the value is not null since EpochProcessors will return 0 on initial processing
            if (value < 0 || min < 0 || max < 0 || sumOfSquares < 0)
            {
                throw new ArgumentException("New Relic Platform does not currently support negative values");
            }

            _requestData.AddMetric(guid, componentName, metricName, units, value, count, min, max, sumOfSquares);
        }

        /// <summary>
        /// Will send all metrics that were reported to the Context since its last successful delivery to the New Relic service.
        /// The context will aggregate values for calls that fail due to transient issues.
        /// </summary>
        public void SendMetricsToService()
        {

            // Check to see if the Agent data is valid before sending data
            if (!ValidateRequestData())
            {
                return;
            }

            var request = (HttpWebRequest)WebRequest.Create(this.ServiceUri);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Headers["X-License-Key"] = this.LicenseKey;

            using (var writer = new StreamWriter(request.GetRequestStream()))
            {
                var str = JsonHelper.Serialize(_requestData.Serialize());
                writer.Write(str);
            }

            HandleServiceResponse(request);
        }

        private bool ValidateRequestData()
        {
            // Do not send any data if no agents reported
            if (!_requestData.HasComponents())
            {
                _requestData.Reset(); // Reset the aggregation timer
                return false;
            }

            // Reset the agent data if we have been aggregating for too long
            if (_requestData.IsPastAggregationLimit())
            {
                _requestData.Reset(); // Reset the aggregation timer
                return false;
            }

            return true;
        }

        private void HandleServiceResponse(HttpWebRequest request)
        {
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            // Reset aggregation after successful delivery
                            _requestData.Reset();
                        }
                    }
                }
            }
            catch (WebException we)
            {
                if( we.Response != null )
                {
                    using (var response = (HttpWebResponse)we.Response)
                    {
                        using( var reader = new StreamReader( response.GetResponseStream() ) )
                        {
                            var body = reader.ReadToEnd();

                            if( response.StatusCode == HttpStatusCode.ServiceUnavailable )
                            {
                                // Collector is being updated
                            }
                            else if( response.StatusCode == HttpStatusCode.Forbidden
                                && string.Equals( Constants.DisableNewRelic, body ) )
                            {
                                // Remotely disabled
                            }
                            else
                            {
                                // Log unknown exception
                                // Rethrow if this is a client exception, keep trying if it is a server error
                                if( (int)response.StatusCode >= 400 && (int)response.StatusCode < 500 )
                                {
                                    throw new NewRelicServiceException( response.StatusCode, body, we );
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Log unknown exception
                }
            } // End catch()
        } // End HandleServiceResponse()
    } // End Context
}
