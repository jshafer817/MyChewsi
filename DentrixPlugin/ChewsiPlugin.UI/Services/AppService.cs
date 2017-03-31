﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using ChewsiPlugin.Api;
using ChewsiPlugin.Api.Chewsi;
using ChewsiPlugin.Api.Common;
using ChewsiPlugin.Api.Dentrix;
using ChewsiPlugin.Api.Interfaces;
using ChewsiPlugin.Api.Repository;
using ChewsiPlugin.UI.ViewModels;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Threading;
using Microsoft.Win32;
using NLog;
using SaveFileDialog = System.Windows.Forms.SaveFileDialog;

namespace ChewsiPlugin.UI.Services
{
    public class AppService : ViewModelBase, IAppService, IDisposable
    {
        private const int RefreshIntervalMs = 10000;
        private const int AppointmentTtlDays = 1;
        private const string ChewsiLauncherRegistryKey = "Chewsi Launcher";
        private const string ChewsiLauncherExecutableName = "ChewsiPlugin.Launcher.exe";

        private static class StatusMessage
        {
            public const string PaymentProcessing = "Payment processing...";
            public const string PaymentProcessingError = "Payment processing failed";
            public const string ReadyToSubmit = "Please submit this claim..";
        }

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IChewsiApi _chewsiApi;
        private readonly IRepository _repository;
        private readonly IDialogService _dialogService;
        private IDentalApi _dentalApi;
        private Settings.PMS.Types _pmsType;
        private readonly object _dentalApiInitializationLock = new object();
        private readonly CancellationTokenSource _tokenSource;
        private bool _loadingAppointments;
        private bool _appointmentsLoaded;
        private bool _statusLookupIsRunning;
        private readonly object _appointmentsLockObject = new object();

        public AppService(IChewsiApi chewsiApi, IRepository repository, IDialogService dialogService)
        {
            _chewsiApi = chewsiApi;
            _repository = repository;
            _dialogService = dialogService;
            _repository.Initialize();

            ClaimItems = new ObservableCollection<ClaimItemViewModel>();
            _tokenSource = new CancellationTokenSource();
        }

        public bool Initialized => _repository.Initialized && DentalApi != null;
        
        public void SaveSettings(SettingsDto settingsDto)
        {
            string pmsVersionOld = null;
            Settings.PMS.Types pmsTypeOld = Settings.PMS.Types.Dentrix;
            string address1Old = null;
            string address2Old = null;
            string stateOld = null;
            string osOld = null;
            string pluginVersionOld = null;
            string tinOld = null;

            var firstAppRun = !_repository.Initialized;
            if (!firstAppRun)
            {
                // DB already has all the settings, load current values to detect changes below
                pmsVersionOld = _repository.GetSettingValue<string>(Settings.PMS.VersionKey);
                pmsTypeOld = _repository.GetSettingValue<Settings.PMS.Types>(Settings.PMS.TypeKey);
                address1Old = _repository.GetSettingValue<string>(Settings.Address1Key);
                address2Old = _repository.GetSettingValue<string>(Settings.Address2Key);
                stateOld = _repository.GetSettingValue<string>(Settings.StateKey);
                osOld = _repository.GetSettingValue<string>(Settings.OsKey);
                pluginVersionOld = _repository.GetSettingValue<string>(Settings.AppVersionKey);
                tinOld = _repository.GetSettingValue<string>(Settings.TIN);
            }

            _repository.SaveSetting(Settings.TIN, settingsDto.Tin);
            _repository.SaveSetting(Settings.PMS.TypeKey, settingsDto.PmsType);
            _repository.SaveSetting(Settings.PMS.PathKey, settingsDto.PmsPath);
            _repository.SaveSetting(Settings.Address1Key, settingsDto.Address1);
            _repository.SaveSetting(Settings.Address2Key, settingsDto.Address2);
            _repository.SaveSetting(Settings.StateKey, settingsDto.State);
            _repository.SaveSetting(Settings.OsKey, Utils.GetOperatingSystemInfo());
            _repository.SaveSetting(Settings.AppVersionKey, Utils.GetPluginVersion());

            _repository.SaveSetting(Settings.StartPms, settingsDto.StartPms);
            _repository.SaveSetting(Settings.UseProxy, settingsDto.UseProxy);
            _repository.SaveSetting(Settings.ProxyAddress, settingsDto.ProxyAddress);
            _repository.SaveSetting(Settings.ProxyPort, settingsDto.ProxyPort);
            _repository.SaveSetting(Settings.ProxyLogin, settingsDto.ProxyLogin);
            _repository.SaveSetting(Settings.ProxyPassword, settingsDto.ProxyPassword);

            // init DentalApi and get version
            _repository.SaveSetting(Settings.PMS.VersionKey, DentalApi.GetVersion());

            // start or kill launcher
            var currentLaunchSetting = GetLauncherStartup();
            if (currentLaunchSetting ^ settingsDto.StartLauncher)
            {
                if (settingsDto.StartLauncher)
                {
                    StartLauncher();
                }
                else
                {
                    KillLauncher();
                }
            }
            SetLauncherStartup(settingsDto.StartLauncher);
            
            // DB is empty, settings saved, call RegisterPlugin
            var registeredBefore = !string.IsNullOrEmpty(_repository.GetSettingValue<string>(Settings.MachineIdKey));
            if (!firstAppRun || !registeredBefore)
            {
                var pmsVersion = DentalApi.GetVersion();
                if (registeredBefore)
                {
                    // detect changes and call UpdatePluginRegistration
                    if ((pmsVersion != pmsVersionOld)
                        || pmsTypeOld != settingsDto.PmsType
                        || address1Old != settingsDto.Address1
                        || address2Old != settingsDto.Address2
                        || stateOld != settingsDto.State
                        || osOld != Utils.GetOperatingSystemInfo()
                        || tinOld != settingsDto.Tin
                        || pluginVersionOld != Utils.GetPluginVersion())
                    {
                        Logger.Info("Configuration changes detected. Updating plugin registration...");
                        var machineId = _repository.GetSettingValue<string>(Settings.MachineIdKey);
                        InitializeChewsiApi();
                        _chewsiApi.UpdatePluginRegistration(new UpdatePluginRegistrationRequest(machineId, settingsDto.Address1, settingsDto.Address2, settingsDto.PmsType, pmsVersion));
                        Logger.Info("Plugin registration successfuly updated");
                    }                    
                }
                else
                {
                    Logger.Info("Registering plugin...");
                    var machineId = _chewsiApi.RegisterPlugin(new RegisterPluginRequest(settingsDto.Tin, settingsDto.Address1, settingsDto.Address2, settingsDto.PmsType, pmsVersion));
                    Logger.Info("Machine Id: " + machineId);
                    _repository.SaveSetting(Settings.MachineIdKey, machineId);    
                    InitializeChewsiApi();                                   
                }
            }
        }

        private void StartLauncher()
        {
            var currentSessionId = Process.GetCurrentProcess().SessionId;
            Process[] runningProcesses = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ChewsiLauncherExecutableName));
            if (runningProcesses.All(m => m.SessionId != currentSessionId))
            {
                try
                {
                    Process.Start(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), ChewsiLauncherExecutableName));
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to start launcher");
                }
            }
        }
        private void KillLauncher()
        {
            var currentSessionId = Process.GetCurrentProcess().SessionId;
            Process process = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ChewsiLauncherExecutableName)).FirstOrDefault(m => m.SessionId == currentSessionId);
            if (process != null)
            {
                try
                {
                    process.Kill();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to kill launcher");
                }
            }
        }

        public void InitializeChewsiApi()
        {
            var token = _repository.GetSettingValue<string>(Settings.MachineIdKey);
            var useProxy = _repository.GetSettingValue<bool>(Settings.UseProxy);
            var proxyAddress = _repository.GetSettingValue<string>(Settings.ProxyAddress);
            var proxyPort = _repository.GetSettingValue<int>(Settings.ProxyPort);
            var proxyUserName = _repository.GetSettingValue<string>(Settings.ProxyLogin);
            var proxyPassword = _repository.GetSettingValue<string>(Settings.ProxyPassword);

            _chewsiApi.Initialize(token, useProxy, proxyAddress, proxyPort, proxyUserName, proxyPassword);
        }

        public SettingsDto GetSettings()
        {
            var pmsType = _repository.GetSettingValue<Settings.PMS.Types>(Settings.PMS.TypeKey);
            var pmsPath = _repository.GetSettingValue<string>(Settings.PMS.PathKey);
            var address1Old = _repository.GetSettingValue<string>(Settings.Address1Key);
            var address2Old = _repository.GetSettingValue<string>(Settings.Address2Key);
            var tin = _repository.GetSettingValue<string>(Settings.TIN);
            var useProxy = _repository.GetSettingValue<bool>(Settings.UseProxy);
            var startPms = _repository.GetSettingValue<bool>(Settings.StartPms);
            var proxyAddress = _repository.GetSettingValue<string>(Settings.ProxyAddress);
            var proxyPassword = _repository.GetSettingValue<string>(Settings.ProxyPassword);
            var proxyPort = _repository.GetSettingValue<int>(Settings.ProxyPort);
            var proxyLogin = _repository.GetSettingValue<string>(Settings.ProxyLogin);
            var state = _repository.GetSettingValue<string>(Settings.StateKey);
            return new SettingsDto(pmsType, pmsPath, address1Old, address2Old, tin, useProxy, proxyAddress, proxyPort, proxyLogin, proxyPassword, state, startPms, GetLauncherStartup());
        }

        public void UpdatePluginRegistration()
        {
            if (DentalApi != null)
            {
                string pmsVersion = DentalApi.GetVersion();
                var pmsVersionOld = _repository.GetSettingValue<string>(Settings.PMS.VersionKey);
                var pmsTypeOld = _repository.GetSettingValue<Settings.PMS.Types>(Settings.PMS.TypeKey);
                var address1Old = _repository.GetSettingValue<string>(Settings.Address1Key);
                var address2Old = _repository.GetSettingValue<string>(Settings.Address2Key);
                var osOld = _repository.GetSettingValue<string>(Settings.OsKey);
                var pluginVersionOld = _repository.GetSettingValue<string>(Settings.AppVersionKey);
                if (pmsVersion != pmsVersionOld
                    || osOld != Utils.GetOperatingSystemInfo()
                    || pluginVersionOld != Utils.GetPluginVersion())
                {
                    Logger.Info("Configuration changes detected. Updating plugin registration...");
                    var machineId = _repository.GetSettingValue<string>(Settings.MachineIdKey);
                    _chewsiApi.UpdatePluginRegistration(new UpdatePluginRegistrationRequest(machineId, address1Old,
                        address2Old, pmsTypeOld, pmsVersion));
                    Logger.Info("Plugin registration successfuly updated");
                }
            }
        }

        private ValidateSubscriberAndProviderResponse ValidateClaim(string providerId, string patientId,
            out ProviderInformation providerInformation, out SubscriberInformation subscriberInformation,
            out Provider provider)
        {
            provider = DentalApi.GetProvider(providerId);
            if (provider != null)
            {
                var patientInfo = DentalApi.GetPatientInfo(patientId);
                if (patientInfo != null)
                {
                    providerInformation = new ProviderInformation
                    {
                        NPI = provider.Npi,
                        TIN = provider.Tin
                    };
                    subscriberInformation = new SubscriberInformation
                    {
                        Id = patientInfo.ChewsiId,
                        SubscriberDateOfBirth = patientInfo.BirthDate,
                        SubscriberFirstName = patientInfo.SubscriberFirstName,
                        SubscriberLastName = patientInfo.SubscriberLastName,
                        PatientLastName = patientInfo.PatientLastName,
                        PatientFirstName = patientInfo.PatientFirstName
                    };
                    var providerAddress = new ProviderAddressInformation
                    {
                        RenderingAddress1 = provider.AddressLine1,
                        RenderingAddress2 = provider.AddressLine2,
                        RenderingCity = provider.City,
                        RenderingState = provider.State,
                        RenderingZip = provider.ZipCode,
                    };
                    try
                    {
                        var validationResponse = _chewsiApi.ValidateSubscriberAndProvider(providerInformation,
                            providerAddress, subscriberInformation);
                        if (validationResponse != null)
                        {
                            Logger.Debug(
                                $"Validated subscriber '{patientId}' and provider '{providerId}': '{validationResponse.ValidationPassed}'");
                        }
                        else
                        {
                            Logger.Debug(
                                $"Failed to validate subscriber '{patientId}' and provider '{providerId}': invalid server response");
                        }
                        return validationResponse;
                    }
                    catch (NullReferenceException)
                    {
                        _dialogService.Show("Invalid server response. Try again later", "Error");
                    }
                }
                else
                {
                    var msg = "Cannot find patient " + patientId;
                    _dialogService.Show(msg, "Error");
                    Logger.Error(msg);
                }
            }
            else
            {
                var msg = "Cannot find provider " + providerId;
                _dialogService.Show(msg, "Error");
                Logger.Error(msg);
            }
            providerInformation = null;
            subscriberInformation = null;
            return null;
        }

        private void SubmitClaim(string chewsiId, DateTime appointmentDate, string patientId,
            ProviderInformation providerInformation, SubscriberInformation subscriberInformation, Provider provider)
        {
            // TODO set appt id
            var procedures = DentalApi.GetProcedures(patientId, null);
            if (procedures.Any())
            {
                _chewsiApi.ProcessClaim(providerInformation, subscriberInformation, procedures.Select(m => new ClaimLine(m.Date, m.Code, m.Amount)).ToList());
                SetAppointmentState(chewsiId, appointmentDate, AppointmentState.ValidationCompletedAndClaimSubmitted);
                RaiseIsProcessingPaymentGetter();
                RefreshAppointments(false);
                if (!_statusLookupIsRunning)
                {
                    _statusLookupIsRunning = true;
                    Task.Factory.StartNew(StatusLookup, _tokenSource.Token);
                }
                Logger.Debug($"Processed claim, found '{procedures.Count}' procedures.");
            }
            else
            {
                var msg = "Cannot find any procedures for the patient";
                _dialogService.Show(msg, "Error");
                Logger.Error(msg + " " + patientId);
            }
        }

        private IDentalApi DentalApi
        {
            get
            {
                var pmsTypeString = _repository.GetSettingValue<string>(Settings.PMS.TypeKey);
                if (pmsTypeString != null)
                {
                    var pmsType = (Settings.PMS.Types)Enum.Parse(typeof(Settings.PMS.Types), pmsTypeString);
                    lock (_dentalApiInitializationLock)
                    {
                        if (_dentalApi == null || pmsType != _pmsType)
                        {
                            // free resources when user changes PMS type
                            if (_dentalApi != null && pmsType != _pmsType)
                            {
                                _dentalApi.Unload();
                            }
                            // create new instance of API on app start and settings changes
                            Logger.Debug("Initializing {0} API", pmsTypeString);
                            switch (pmsType)
                            {
                                case Settings.PMS.Types.Dentrix:
                                    _dentalApi = new DentrixApi(_dialogService);
                                    break;
                                case Settings.PMS.Types.OpenDental:
                                    _dentalApi = new OpenDentalApi.OpenDentalApi(_repository, _dialogService);
                                    break;
                                case Settings.PMS.Types.Eaglesoft:
                                    _dentalApi = new EaglesoftApi.EaglesoftApi(_dialogService);
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                            _pmsType = pmsType;
                            CommandManager.InvalidateRequerySuggested();
                        }
                        return _dentalApi;                        
                    }
                }
                Logger.Error("PMS system type is not set");
                return null;
            }
        }

        private void StatusLookup()
        {
            // stop if already canceled
            _tokenSource.Token.ThrowIfCancellationRequested();

            while (true)
            {
                if (_tokenSource.Token.IsCancellationRequested)
                {
                    _tokenSource.Token.ThrowIfCancellationRequested();
                }
                lock (_appointmentsLockObject)
                {
                    // find providers for submitted appointments
                    foreach (var providerId in _repository.GetAppointments().Where(m => m.State == AppointmentState.ValidationCompletedAndClaimSubmitted).GroupBy(m => m.ProviderId).Select(m => m.Key).Distinct())
                    {
                        var provider = DentalApi.GetProvider(providerId);
                        var request = new ClaimProcessingStatusRequest
                        {
                            TIN = provider.Tin,
                            State = provider.State,
                            Address = provider.AddressLine1
                        };
                        var statusResponse = _chewsiApi.GetClaimProcessingStatus(request);
                        if (statusResponse != null)
                        {
                            foreach (ClaimStatus claimStatus in statusResponse)
                            {
                                var viewModel = ClaimItems.FirstOrDefault(m => m.IsClaimStatus && claimStatus.Claim_Nbr == m.ClaimNumber);
                                if (viewModel == null)
                                {
                                    var model = new ClaimItemViewModel(this)
                                    {
                                        ClaimNumber = claimStatus.Claim_Nbr,
                                        ChewsiId = claimStatus.ChewsiID,
                                        State = AppointmentState.ValidationCompletedAndClaimSubmitted,
                                        StatusText = claimStatus.MessageToDisplay,
                                        Date = claimStatus.PostedOnDate,
                                        SubscriberFirstName = claimStatus.SubscriberFirstName,
                                        // PatientId = ,
                                        ProviderId = providerId,
                                        IsClaimStatus = true,
                                        PatientName = claimStatus.PatientFirstName
                                    };
                                    if (claimStatus.Status == "GeneralError" || claimStatus.Status == "ValidationError")
                                    {
                                        model.State = AppointmentState.ValidationError;
                                    }
                                    else if (claimStatus.Status == "InvalidProcedureCode")
                                    {
                                        model.State = AppointmentState.ValidationErrorNoResubmit;
                                    }
                                    DispatcherHelper.CheckBeginInvokeOnUI(() => ClaimItems.Add(model));
                                }
                                else
                                {
                                    viewModel.StatusText = claimStatus.MessageToDisplay;
                                }
                            }
                        }
                        else
                        {
                            _dialogService.Show("Error getting claim status. Bad server response.");
                        }
                    }
                }
                Thread.Sleep(RefreshIntervalMs);
            }
        }

        public void Dispose()
        {
            _tokenSource.Cancel();
        }

        public void RefreshAppointments(bool loadFromPms)
        {
            DispatcherHelper.CheckBeginInvokeOnUI(() =>
            {
                if (!_loadingAppointments)
                {
                    lock (_appointmentsLockObject)
                    {
                        if (!_loadingAppointments)
                        {
                            _loadingAppointments = true;
                            _dialogService.ShowLoadingIndicator();
                            try
                            {
                                var list = LoadAppointments(loadFromPms);

                                var existingList = ClaimItems.ToList();

                                // add new rows
                                foreach (var item in list)
                                {
                                    bool exists = false;
                                    foreach (var viewModel in existingList)
                                    {
                                        if (item.Equals(viewModel))
                                        {
                                            exists = true;
                                            // update existing view model
                                            viewModel.State = item.State;
                                            viewModel.StatusText = item.StatusText;
                                            viewModel.SubscriberFirstName = item.SubscriberFirstName;
                                            break;
                                        }
                                    }
                                    if (!exists)
                                    {
                                        existingList.Add(item);
                                    }
                                }
                                ClaimItems.Clear();

                                // remove old
                                foreach (var item in existingList)
                                {
                                    if (list.Any(m => item.Equals(m)))
                                    {
                                        ClaimItems.Add(item);
                                    }
                                }
                                _dialogService.HideLoadingIndicator();

                            }
                            finally
                            {
                                _loadingAppointments = false;
                                _appointmentsLoaded = true;
                            }
                        }
                    }
                }
            });
        }

        public ObservableCollection<ClaimItemViewModel> ClaimItems { get; private set; }

        public bool IsProcessingPayment
        {
            get { return ClaimItems.Any(m => m.State == AppointmentState.ValidationCompletedAndClaimSubmitted); }
        }

        public bool IsLoadingAppointments { get { return _loadingAppointments; } }
        public bool AppointmentsLoaded { get { return _appointmentsLoaded; } }

        private void RaiseIsProcessingPaymentGetter()
        {
            OnStartPaymentStatusLookup?.Invoke();
        }

        /// <summary>
        /// Loads appointments from PMS (optionally), caches them in local DB
        /// </summary>
        private List<ClaimItemViewModel> LoadAppointments(bool loadFromPms)
        {
            try
            {
                // load cached in repository items
                var cached = _repository.GetAppointments();

                if (loadFromPms)
                {
                    // load from PMS
                    List<IAppointment> pms = DentalApi.GetAppointmentsForToday();
                    // pms.ForEach(m => Logger.Debug($"Loaded appointment from PMS: Date={m.Date}, ChewsiId={m.ChewsiId}"));
                    // cached.ForEach(m => Logger.Debug($"Cached appointment: Date={m.DateTime}, ChewsiId={m.ChewsiId}"));

                    // load subscribers' first names (they are displayed if validation fails)
                    var subscriberNames = pms.Select(m => m.PatientId)
                        .Distinct()
                        .Select(m => DentalApi.GetPatientInfo(m))
                        .GroupBy(m => m.ChewsiId)
                        .ToDictionary(m => m.Key, m => m.First().SubscriberFirstName);

                    bool repositoryUpdated = false;

                    // merge PMS items with cached in repository

                    // add new appointments into repository, exist in PMS, not in the DB
                    foreach (var item in pms)
                    {
                        var cachedItem = cached.FirstOrDefault(m => item.ChewsiId == m.ChewsiId && item.Date == m.DateTime);
                        if (cachedItem == null)
                        {
                            Logger.Debug($"Adding new appointment in the cache ChewsiId={item.ChewsiId}");
                            _repository.AddAppointment(new Appointment
                            {
                                DateTime = item.Date,
                                State = AppointmentState.TreatmentCompleted,
                                ChewsiId = item.ChewsiId,
                                StatusText = StatusMessage.ReadyToSubmit,
                                PatientId = item.PatientId,
                                PatientName = item.PatientName,
                                ProviderId = item.ProviderId,
                                SubscriberFirstName = subscriberNames[item.ChewsiId]
                            });
                            repositoryUpdated = true;
                        }
                    }

                    // remove old, exist in the DB, not in PMS
                    var old = (from item in cached
                        where pms.All(m => item.ChewsiId != m.ChewsiId)
                        select item.Id).ToList();
                    if (old.Any())
                    {
                        _repository.BulkDeleteAppointments(old);
                        repositoryUpdated = true;
                    }

                    if (repositoryUpdated)
                    {
                        // re-read list from repository
                        cached = _repository.GetAppointments();
                    }
                }

                var result =
                    (from c in cached
                        where c.State != AppointmentState.Deleted && c.State != AppointmentState.ValidationCompletedAndClaimSubmitted
                        orderby c.State == AppointmentState.TreatmentCompleted descending
                        select new ClaimItemViewModel(this)
                        {
                            ProviderId = c.ProviderId,
                            Date = c.DateTime,
                            PatientName = c.PatientName,
                            ChewsiId = c.ChewsiId,
                            State = c.State,
                            StatusText = c.StatusText,
                            PatientId = c.PatientId,
                            SubscriberFirstName = c.SubscriberFirstName
                        }).ToList();
                if (Logger.IsDebugEnabled)
                {
                    result.ForEach(m => Logger.Debug($"Loaded appointment VM ChewsiId={m.ChewsiId}, State={m.State}"));
                }
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to load appointments");
            }
            return new List<ClaimItemViewModel>();
        }

        public void DeleteAppointment(string chewsiId, DateTime date)
        {
            lock (_appointmentsLockObject)
            {
                var existing = _repository.GetAppointmentByChewsiIdAndDate(chewsiId, date);
                if (existing != null)
                {
                    existing.State = AppointmentState.Deleted;
                    _repository.UpdateAppointment(existing);
                    RefreshAppointments(false);
                }
            }
        }
        
        private void SetAppointmentState(string chewsiId, DateTime appointmentDate, AppointmentState? state = null, string statusText = null)
        {
            bool updated = false;
            // call by dispatcher to make sure update is done by one thread
            DispatcherHelper.RunAsync(() =>
            {
                lock (_appointmentsLockObject)
                {
                    var existing = _repository.GetAppointmentByChewsiIdAndDate(chewsiId, appointmentDate);
                    if (existing != null)
                    {
                        if (state.HasValue && existing.State != state.Value)
                        {
                            existing.State = state.Value;
                            updated = true;
                        }
                        if (statusText != null && existing.StatusText != statusText)
                        {
                            existing.StatusText = statusText;
                            updated = true;
                        }
                        _repository.UpdateAppointment(existing);
                    }
                }
                if (updated)
                {
                    RefreshAppointments(false);
                }
            });
        }

        public void DeleteOldAppointments()
        {
            lock (_appointmentsLockObject)
            {
                var list = _repository.GetAppointments();
                var date = DateTime.Now.Date.AddDays(-AppointmentTtlDays);
                var old = list.Where(m => m.DateTime < date).Select(m => m.Id).ToList();
                if (old.Any())
                {
                    Logger.Info("Deleting {0} old cached appointments", old.Count);
                    _repository.BulkDeleteAppointments(old);
                }
            }
        }

        public event Action OnStartPaymentStatusLookup;

        public void ValidateAndSubmitClaim(string chewsiId, DateTime date, string providerId, string patientId, Action callEndCallback)
        {
            ProviderInformation providerInformation = null;
            SubscriberInformation subscriberInformation = null;
            Provider provider = null;
            ValidateSubscriberAndProviderResponse validationResponse = null;
            Task.Factory.StartNew(() =>
            {
                SetAppointmentState(chewsiId, date, AppointmentState.TreatmentCompleted, StatusMessage.PaymentProcessing);
                validationResponse = ValidateClaim(providerId, patientId, out providerInformation, out subscriberInformation, out provider);
            }).ContinueWith(t =>
            {
                if (!t.IsFaulted)
                {
                    if (validationResponse != null)
                    {
                        if (validationResponse.ValidationPassed)
                        {
                            providerInformation.Id = validationResponse.ProviderID;
                            providerInformation.OfficeNbr = validationResponse.OfficeNumber;
                            
                            SubmitClaim(chewsiId, date, patientId, providerInformation, subscriberInformation, provider);
                        }
                        else
                        {
                            #region Format status
                            var statusText = "";
                            if (!string.IsNullOrEmpty(validationResponse.ProviderValidationMessage))
                            {
                                statusText += validationResponse.ProviderValidationMessage;
                            }
                            if (!string.IsNullOrEmpty(validationResponse.SubscriberValidationMessage))
                            {
                                if (!string.IsNullOrEmpty(statusText))
                                {
                                    statusText += Environment.NewLine;
                                }
                                statusText += validationResponse.SubscriberValidationMessage;
                            }
                            #endregion
                            
                            SetAppointmentState(chewsiId, date, AppointmentState.ValidationError, statusText);
                        }
                    }
                    else
                    {
                        SetAppointmentState(chewsiId, date, AppointmentState.ValidationError, StatusMessage.PaymentProcessingError);
                    }
                }
                callEndCallback();
            });
        }

        public void StartPmsIfRequired()
        {
            var start = _repository.GetSettingValue<bool>(Settings.StartPms);
            if (start)
            {
                DentalApi.Start();
            }
        }
        
        public void DownloadFile(string documentId, string postedDate, bool downloadReport)
        {
            _dialogService.ShowLoadingIndicator();
            try
            {
                var tin = _repository.GetSettingValue<string>(Settings.TIN);
                var stream = _chewsiApi.DownloadFile(new DownoadFileRequest
                {
                    DocumentType = downloadReport ? DownoadFileType.Pdf : DownoadFileType.Txt,
                    TIN = tin,
                    DocumentID = documentId,
                    PostedOnDate = postedDate
                });
                if (stream != null)
                {
                    var dialog = new SaveFileDialog
                    {
                        FileName = downloadReport ? $"Report_{postedDate.Replace('/', '-')}.pdf" :  $"EDI_{postedDate.Replace('/', '-')}.txt",
                        Title = downloadReport ? "Save report file" : "Save 835 file",
                        Filter = downloadReport ? "PDF file (*.pdf)|*.pdf" : "Text file (*.txt)|*.txt"
                    };
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        using (FileStream file = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write))
                        {
                            stream.CopyTo(file);
                            stream.Close();
                        }
                        _dialogService.Show("File successfully downloaded", "Information");
                    }                    
                }
                else
                {
                    _dialogService.Show("Error downloading file. Try again later.", "Error");
                }
            }
            finally
            {
                _dialogService.HideLoadingIndicator();
            }
        }

        public Provider GetProvider()
        {
            var claim = ClaimItems.FirstOrDefault();
            var providerId = claim?.ProviderId;
            if (providerId != null)
            {
                return DentalApi.GetProvider(providerId);
            }
            return null;
        }

        private bool GetLauncherStartup()
        {
            try
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", false);
                if (key != null)
                {
                    return key.GetValue(ChewsiLauncherRegistryKey) != null;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Cannot load launcher startup settings from registry");
            }
            return false;
        }

        public void SetLauncherStartup(bool enabled)
        {
            try
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (key != null)
                {
                    if (enabled)
                    {
                        if (key.GetValue(ChewsiLauncherRegistryKey) == null)
                        {
                            key.SetValue(ChewsiLauncherRegistryKey, Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), ChewsiLauncherExecutableName));
                        }
                    }
                    else
                    {
                        key.DeleteValue(ChewsiLauncherRegistryKey, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Cannot save launcher startup settings in registry");
            }
        }
    }
}
