﻿using System;
using GalaSoft.MvvmLight;

namespace ChewsiPlugin.UI.ViewModels
{
    public class ClaimItemViewModel : ViewModelBase
    {
        private DateTime _date;
        private string _chewsiId;
        private string _patient;
        private string _provider;
        private string _patientId;
        private string _status;

        public DateTime Date
        {
            get { return _date; }
            set
            {
                _date = value;
                RaisePropertyChanged(() => Date);
            }
        }

        public string ChewsiId
        {
            get { return _chewsiId; }
            set
            {
                _chewsiId = value;
                RaisePropertyChanged(() => ChewsiId);
            }
        }

        public string Patient
        {
            get { return _patient; }
            set
            {
                _patient = value;
                RaisePropertyChanged(() => Patient);
            }
        }

        public string Provider
        {
            get { return _provider; }
            set
            {
                _provider = value;
                RaisePropertyChanged(() => Provider);
            }
        }

        public string PatientId
        {
            get { return _patientId; }
            set
            {
                _patientId = value;
                RaisePropertyChanged(() => PatientId);
            }
        }

        public string Status
        {
            get { return _status; }
            set
            {
                _status = value;
                RaisePropertyChanged(() => Status);
            }
        }

        public bool Equals(ClaimItemViewModel item)
        {
            return Date == item.Date
                   && PatientId == item.PatientId
                   && ChewsiId == item.ChewsiId
                   && Provider == item.Provider;
        }
    }
}