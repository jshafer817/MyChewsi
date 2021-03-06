﻿using System;
using System.Reflection;
using System.ServiceModel;
using System.Threading;
using NLog;

namespace ChewsiPlugin.Api.Common
{
    public static class Utils
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string Address = "http://{0}:45000/DentalApi.svc";

        public static string GetPluginVersion()
        {
            return Assembly.GetCallingAssembly().GetName().Version.ToString();
        }

        public static string GetAddressFromHost(string serverHost)
        {
            if (serverHost == null)
            {
                return null;
            }
            return string.Format(Address, serverHost);
        }

        public static string GetHostFromAddress(string address)
        {
            if (address == null)
            {
                return null;
            }
            return new Uri(address).Host;
        }

        public static string GetOperatingSystemInfo()
        {
            return Environment.OSVersion.ToString();
        }

        /// <summary>
        /// Compares two DateTimes with 1s accuracy
        /// </summary>
        public static bool ArePmsModifiedDatesEqual(DateTime d1, DateTime d2)
        {
            return Math.Abs((d1 - d2).TotalSeconds) < 1;
        }

        public static void SleepWithCancellation(CancellationToken token, int sleepMs)
        {
            for (int i = 0; i < sleepMs / 100; i++)
            {
                Thread.Sleep(100);
                if (token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                }                
            }
        }

        public static bool AreAmountsEqual(double d1, double d2)
        {
            return Math.Abs(d1 - d2) < 0.01;
        }
        
        public static bool SafeCall(Action action)
        {
            try
            {
                action.Invoke();
                return true;
            }
            catch (TimeoutException)
            {
                Logger.Warn("Service timeout");
            }
            catch (FaultException ex)
            {
                Logger.Warn(ex, "Exception handled on server side");
            }
            catch (CommunicationException ex)
            {
                Logger.Warn(ex, "Communication error");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unexpected WCF error");
            }
            return false;
        }

        public static bool TrySafeCall<T>(Func<T> action, out T result)
        {
            try
            {
                result = action.Invoke();
                return true;
            }
            catch (TimeoutException)
            {
                Logger.Warn("Service timeout");
            }
            catch (FaultException ex)
            {
                Logger.Warn(ex, "Exception handled on server side");
            }
            catch (CommunicationException ex)
            {
                Logger.Warn(ex, "Communication error");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unexpected WCF error");
            }
            result = default(T);
            return false;
        }

        public static string FormatForApiRequest(this DateTime value)
        {
            return value.ToString("MM/dd/yyyy HH:mm:ss tt");
        }
    }
}
