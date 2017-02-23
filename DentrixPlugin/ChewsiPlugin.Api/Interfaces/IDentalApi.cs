﻿using System.Collections.Generic;
using ChewsiPlugin.Api.Common;
using ChewsiPlugin.Api.Repository;

namespace ChewsiPlugin.Api.Interfaces
{
    public interface IDentalApi
    {
        PatientInfo GetPatientInfo(string patientId);
        List<ProcedureInfo> GetProcedures(string patientId);
        List<IAppointment> GetAppointmentsForToday();
        Provider GetProvider(string providerId);
        string GetVersion();
        bool IsInstalled(out string folder);
        string Name { get; }
        Settings.PMS.Types Type { get; }
        void Unload();
        bool Initialized { get; }
    }
}
