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

        public static string GetPluginVersion()
        {
            return Assembly.GetCallingAssembly().GetName().Version.ToString();
        }

        public static string GetOperatingSystemInfo()
        {
            return Environment.OSVersion.ToString();
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

        public static bool SafeCall<T>(Action<T> action, T arg)
        {
            try
            {
                action.Invoke(arg);
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

        public static bool SafeCall<T1,T2,T3>(Action<T1,T2,T3> action, T1 arg1, T2 arg2, T3 arg3)
        {
            try
            {
                action.Invoke(arg1, arg2, arg3);
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

        public static bool TrySafeCall<T, R>(Func<T, R> action, T arg, out R result)
        {
            try
            {
                result = action.Invoke(arg);
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
            result = default(R);
            return false;
        }
    }
}
