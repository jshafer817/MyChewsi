﻿using System;

namespace ChewsiPlugin.Api
{
    public class ClaimStatus
    {
        public DateTime Date { get; set; }
        public string Subscriber { get; set; }
        public string Provider { get; set; }
        public ClaimStatusType Status { get; set; }
    }
}