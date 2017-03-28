﻿using System;
using System.Windows.Forms;
using System.Windows.Input;
using ChewsiPlugin.Api.Interfaces;
using ChewsiPlugin.Api.Repository;
using ChewsiPlugin.UI.Services;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using NLog;

namespace ChewsiPlugin.UI.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IAppService _appService;
        private readonly Action _onClose;
        private readonly IDialogService _dialogService;
        private Settings.PMS.Types _selectedType;
        private string _path;
        private ICommand _closeCommand;
        private string _address1;
        private string _address2;
        private string _tin;
        private bool _useProxy;
        private string _proxyAddress;
        private int _proxyPort;
        private string _proxyLogin;
        private string _proxyPassword;
        private ICommand _saveCommand;
        private ICommand _selectPath;
        private string _state;
        private bool _startPms;

        public SettingsViewModel(IAppService appService, Action onClose, IDialogService dialogService)
        {
            _appService = appService;
            _onClose = onClose;
            _dialogService = dialogService;
            Types = new[] {Settings.PMS.Types.Dentrix, Settings.PMS.Types.Eaglesoft, Settings.PMS.Types.OpenDental };

            var s = _appService.GetSettings();
            _address1 = s.Address1;
            _address2 = s.Address2;
            _tin = s.Tin;
            _useProxy = s.UseProxy;
            _proxyAddress = s.ProxyAddress;
            _proxyPort = s.ProxyPort;
            _proxyLogin = s.ProxyLogin;
            _proxyPassword = s.ProxyPassword;
            _selectedType = s.PmsType;
            _path = s.PmsPath;
            _state = s.State;
            _startPms = s.StartPms;
        }

        public Settings.PMS.Types[] Types { get; private set; }

        public Settings.PMS.Types SelectedType
        {
            get { return _selectedType; }
            set
            {
                _selectedType = value;
                RaisePropertyChanged(() => SelectedType);
                RaisePropertyChanged(() => NeedsPath);
            }
        }

        public string Path
        {
            get { return _path; }
            set
            {
                _path = value;
                RaisePropertyChanged(() => Path);
            }
        }

        public string Address1
        {
            get { return _address1; }
            set
            {
                _address1 = value;
                RaisePropertyChanged(() => Address1);
            }
        }

        public string Address2
        {
            get { return _address2; }
            set
            {
                _address2 = value;
                RaisePropertyChanged(() => Address2);
            }
        }

        public string State
        {
            get { return _state; }
            set
            {
                _state = value;
                RaisePropertyChanged(() => State);
            }
        }

        public string Tin
        {
            get { return _tin; }
            set
            {
                _tin = value;
                RaisePropertyChanged(() => Tin);
            }
        }

        public bool NeedsPath
        {
            get { return SelectedType == Settings.PMS.Types.OpenDental; }
        }
        
        public bool UseProxy
        {
            get { return _useProxy; }
            set
            {
                _useProxy = value;
                RaisePropertyChanged(() => UseProxy);
            }
        }
        
        public bool StartPms
        {
            get { return _startPms; }
            set
            {
                _startPms = value;
                RaisePropertyChanged(() => StartPms);
            }
        }

        public string ProxyAddress
        {
            get { return _proxyAddress; }
            set
            {
                _proxyAddress = value;
                RaisePropertyChanged(() => ProxyAddress);
            }
        }

        public int ProxyPort
        {
            get { return _proxyPort; }
            set
            {
                _proxyPort = value;
                RaisePropertyChanged(() => ProxyPort);
            }
        }

        public string ProxyLogin
        {
            get { return _proxyLogin; }
            set
            {
                _proxyLogin = value;
                RaisePropertyChanged(() => ProxyLogin);
            }
        }

        public string ProxyPassword
        {
            get { return _proxyPassword; }
            set
            {
                _proxyPassword = value;
                RaisePropertyChanged(() => ProxyPassword);
            }
        }

        #region CloseCommand
        public ICommand CloseCommand
        {
            get { return _closeCommand ?? (_closeCommand = new RelayCommand(OnCloseCommandExecute, CanCloseCommandExecute)); }
        }

        private bool CanCloseCommandExecute()
        {
            return false;
        }

        private void OnCloseCommandExecute()
        {
            _onClose?.Invoke();
        }
        #endregion 
          
        #region SaveCommand
        public ICommand SaveCommand
        {
            get { return _saveCommand ?? (_saveCommand = new RelayCommand(OnSaveCommandExecute)); }
        }
        
        private void OnSaveCommandExecute()
        {
            try
            {
                _dialogService.ShowLoadingIndicator();
                _appService.SaveSettings(new SettingsDto(SelectedType, Path, Address1, Address2, Tin, UseProxy, ProxyAddress, ProxyPort, ProxyLogin, ProxyPassword, State, StartPms));
            }
            finally
            {
                _dialogService.HideLoadingIndicator();
            }
            Logger.Debug("Settings were saved");
            _onClose?.Invoke();
        }
        #endregion   

        #region SelectPath
        public ICommand SelectPath
        {
            get { return _selectPath ?? (_selectPath = new RelayCommand(OnSelectPath, CanSelectPath)); }
        }

        private bool CanSelectPath()
        {
            return NeedsPath;
        }

        private void OnSelectPath()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                DialogResult result = dialog.ShowDialog();
                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                {
                    Path = dialog.SelectedPath;
                }
            }
        }
        #endregion   
    }
}
