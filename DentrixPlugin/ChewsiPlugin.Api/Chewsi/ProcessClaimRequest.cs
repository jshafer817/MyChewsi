﻿using System.Collections.Generic;

namespace ChewsiPlugin.Api.Chewsi
{
    public class ProcessClaimRequest
    {
        public string ProviderID { get; set; }
        public string TIN { get; set; }
        public string NPI { get; set; }
        public string OfficeNbr { get; set; }
        public string SubscriberID { get; set; }
        public string SubscriberFirstName { get; set; }
        public string SubscriberLastName { get; set; }
        public string SubscriberDOB { get; set; }
        public string PIN { get; set; }
        public List<ProcedureInformation> ClaimLines { get; set; }
    }
}