namespace BarrierLedger.SyntheticSapData;

internal sealed record Installation(string Code, string En, string Ar, int WorkCenterSuffix);

internal sealed record PlantUnit(
    string Code, string InstallationCode, string TagNo, string En, string Ar, (string En, string Ar)[] Sections);

/// <summary>One piece of equipment installed at a position.</summary>
internal sealed record EquipTemplate(string ObjectType, string TagPrefix, string En, string Ar, int Percent);

/// <summary>
/// A kind of tag-level functional location. <c>Weights</c> is "UNIT:weight,..." with
/// "*" as the fallback; a unit with no entry and no fallback never gets this type.
/// </summary>
internal sealed record PositionType(
    string Prefix, string En, string Ar, string Discipline, string Weights, bool Paired, EquipTemplate[] Items)
{
    public int WeightFor(string unitCode)
    {
        int fallback = 0;
        foreach (string part in Weights.Split(','))
        {
            string[] kv = part.Split(':');
            if (kv[0] == unitCode)
            {
                return int.Parse(kv[1]);
            }

            if (kv[0] == "*")
            {
                fallback = int.Parse(kv[1]);
            }
        }

        return fallback;
    }
}

internal sealed record ObjectTypeInfo(string Code, string SuggestedClass, string Category);

/// <summary>
/// All invented reference content. Nothing here is taken from a real company,
/// plant, vendor or document.
/// </summary>
internal static class Catalog
{
    public const string DatasetName = "Tal Al Zumurrud Refinery (fictional)";
    public const string Separator = "-";

    // Installation, plant/unit, section, then a tag-level position (sub-level 1 of level 5).
    public const string FlCodeMask = @"^[A-Z]{2}\d(-[A-Z]{3}(-[A-Z]\d{2}(-[A-Z]{1,3}\d{3}[A-Z]?)?)?)?$";

    public const string BuildingUnitCode = "BLD";
    public const string BuildingSubtreeRoot = "TZ1-BLD";

    public static readonly Installation[] Installations =
    {
        new("TZ1", "Tal Al Zumurrud Refinery - Process Area", "مصفاة تل الزمرد - منطقة العمليات", 1),
        new("TZ2", "Tal Al Zumurrud Refinery - Marine Terminal", "مصفاة تل الزمرد - الميناء البحري", 2),
    };

    public static readonly PlantUnit[] ProcessUnits =
    {
        new("CDU", "TZ1", "11", "Crude Distillation Unit", "وحدة تقطير الخام", new[]
        {
            ("Crude Feed and Preheat", "تغذية الخام والتسخين المبدئي"),
            ("Desalter", "نازع الأملاح"),
            ("Crude Furnace", "فرن الخام"),
            ("Atmospheric Column", "برج التقطير الجوي"),
            ("Overhead System", "نظام أعلى البرج"),
            ("Side Strippers", "أبراج النزع الجانبية"),
            ("Pumparound Circuits", "دوائر التدوير"),
            ("Product Rundown", "تصريف المنتجات"),
            ("Chemical Injection", "حقن الكيماويات"),
            ("Flare Knockout", "فاصل الشعلة"),
        }),
        new("VDU", "TZ1", "12", "Vacuum Distillation Unit", "وحدة التقطير الفراغي", new[]
        {
            ("Vacuum Feed", "تغذية وحدة التفريغ"),
            ("Vacuum Furnace", "فرن التفريغ"),
            ("Vacuum Column", "برج التفريغ"),
            ("Ejector System", "نظام القاذفات"),
            ("Overhead Condensers", "مكثفات أعلى البرج"),
            ("Gas Oil Circuits", "دوائر زيت الغاز"),
            ("Residue Handling", "تداول المتبقي"),
            ("Steam Generation", "توليد البخار"),
            ("Slop Oil", "الزيت المرتجع"),
            ("Sour Water", "المياه الحامضية"),
        }),
        new("NHT", "TZ1", "13", "Naphtha Hydrotreater", "وحدة معالجة النافتا بالهيدروجين", new[]
        {
            ("Feed Section", "قسم التغذية"),
            ("Reactor Section", "قسم المفاعل"),
            ("Recycle Gas Compression", "ضغط غاز التدوير"),
            ("Separation", "قسم الفصل"),
            ("Stripper", "برج النزع"),
            ("Splitter", "برج الفصل"),
            ("Hydrogen Makeup", "تعويض الهيدروجين"),
            ("Amine Wash", "الغسيل بالأمين"),
            ("Product Cooling", "تبريد المنتجات"),
            ("Relief and Blowdown", "التنفيس والتصريف"),
        }),
        new("UTL", "TZ1", "21", "Utilities", "المرافق", new[]
        {
            ("Steam System", "نظام البخار"),
            ("Cooling Water", "مياه التبريد"),
            ("Instrument Air", "هواء الأجهزة"),
            ("Nitrogen", "النيتروجين"),
            ("Fire Water", "مياه الحريق"),
            ("Power Distribution", "توزيع القدرة الكهربائية"),
            ("Fuel Gas", "غاز الوقود"),
            ("Water Treatment", "معالجة المياه"),
            ("Effluent Treatment", "معالجة الصرف"),
            ("Flare System", "نظام الشعلة"),
        }),
        new("TKF", "TZ2", "31", "Tank Farm", "منطقة الخزانات", new[]
        {
            ("Crude Tankage", "خزانات الخام"),
            ("Naphtha Tankage", "خزانات النافتا"),
            ("Gas Oil Tankage", "خزانات زيت الغاز"),
            ("Fuel Oil Tankage", "خزانات المازوت"),
            ("Transfer Pumps", "طلمبات النقل"),
            ("Blending", "الخلط"),
            ("Tank Farm Fire Protection", "حماية الخزانات من الحريق"),
            ("Drainage and Sumps", "الصرف والبيارات"),
            ("Metering", "القياس"),
            ("Vapour Recovery", "استرجاع الأبخرة"),
        }),
        new("JTY", "TZ2", "32", "Jetty and Loading", "الرصيف البحري والتحميل", new[]
        {
            ("Berth 1", "الرصيف 1"),
            ("Berth 2", "الرصيف 2"),
            ("Loading Arms", "أذرع التحميل"),
            ("Shore Pipelines", "خطوط الشاطئ"),
            ("Jetty Fire Protection", "حماية الرصيف من الحريق"),
            ("Ballast and Slop", "الصابورة والمرتجع"),
            ("Jetty Utilities", "مرافق الرصيف"),
            ("Custody Metering", "قياس التسليم"),
            ("Emergency Shutdown", "الإيقاف الطارئ"),
            ("Mooring Systems", "أنظمة الرباط"),
        }),
    };

    public static readonly PlantUnit BuildingUnit =
        new(BuildingUnitCode, "TZ1", "90", "Buildings", "المباني", new[]
        {
            ("Administration Building", "مبنى الإدارة"),
            ("Training Centre", "مركز التدريب"),
            ("Canteen", "المطعم"),
        });

    public static readonly PositionType[] ProcessPositionTypes =
    {
        new("P", "Pump", "طلمبة", "MECH", "*:14", true, new EquipTemplate[]
        {
            new("PUMP-CF", "P", "Centrifugal Pump", "طلمبة طرد مركزي", 100),
            new("MOTOR-LV", "PM", "Pump Motor", "محرك طلمبة", 95),
            new("MSEAL", "PS", "Mechanical Seal", "مانع تسرب ميكانيكي", 75),
            new("COUPLING", "PC", "Coupling", "قارنة", 60),
            new("GAUGE-P", "PG", "Pressure Gauge", "مبين ضغط", 70),
        }),
        new("RP", "Reciprocating Pump", "طلمبة ترددية", "MECH", "*:2", false, new EquipTemplate[]
        {
            new("PUMP-RC", "RP", "Reciprocating Pump", "طلمبة ترددية", 100),
            new("MOTOR-LV", "RPM", "Pump Motor", "محرك طلمبة", 100),
            new("PSV", "RPV", "Discharge Relief Valve", "صمام تنفيس الطرد", 90),
            new("DAMPENER", "RPD", "Pulsation Dampener", "مخمد نبضات", 70),
        }),
        new("K", "Compressor", "ضاغط", "MECH", "NHT:3,UTL:2,*:1", false, new EquipTemplate[]
        {
            new("COMP-CF", "K", "Centrifugal Compressor", "ضاغط طرد مركزي", 100),
            new("MOTOR-HV", "KM", "Compressor Motor", "محرك ضاغط", 100),
            new("GEARBOX", "KG", "Gearbox", "صندوق تروس", 70),
            new("PUMP-RC", "KP", "Lube Oil Pump", "طلمبة زيت التزييت", 90),
            new("HX-ST", "KE", "Lube Oil Cooler", "مبرد زيت التزييت", 80),
            new("FILTER", "KF", "Lube Oil Filter", "مرشح زيت التزييت", 80),
        }),
        new("E", "Heat Exchanger", "مبادل حراري", "STAT", "*:10", false, new EquipTemplate[]
        {
            new("HX-ST", "E", "Shell and Tube Heat Exchanger", "مبادل حراري", 100),
            new("PSV", "ERV", "Thermal Relief Valve", "صمام تنفيس حراري", 60),
            new("GAUGE-T", "ETG", "Temperature Gauge", "مبين حرارة", 70),
        }),
        new("AC", "Air Cooler", "مبرد هوائي", "STAT", "*:4", false, new EquipTemplate[]
        {
            new("HX-AC", "AC", "Air Cooled Exchanger", "مبرد هوائي", 100),
            new("FAN", "ACF", "Air Cooler Fan", "مروحة مبرد هوائي", 100),
            new("MOTOR-LV", "ACM", "Fan Motor", "محرك مروحة", 100),
            new("VIB-SW", "ACV", "Vibration Switch", "مفتاح اهتزاز", 80),
        }),
        new("V", "Vessel", "وعاء", "STAT", "*:8", false, new EquipTemplate[]
        {
            new("VESSEL", "V", "Pressure Vessel", "وعاء ضغط", 100),
            new("GAUGE-L", "LG", "Level Gauge", "مبين مستوى", 85),
            new("PSV", "VRV", "Vessel Relief Valve", "صمام أمان الوعاء", 70),
        }),
        new("C", "Column", "برج", "STAT", "CDU:1,VDU:1,NHT:1", false, new EquipTemplate[]
        {
            new("COLUMN", "C", "Distillation Column", "برج تقطير", 100),
            new("PSV", "CRV", "Column Relief Valve", "صمام أمان البرج", 100),
        }),
        new("H", "Fired Heater", "فرن", "STAT", "CDU:1,VDU:1,NHT:1", false, new EquipTemplate[]
        {
            new("HEATER", "H", "Fired Heater", "فرن", 100),
            new("BURNER", "HB", "Burner Assembly", "مجموعة شعلات", 100),
            new("FAN", "HF", "Combustion Air Fan", "مروحة هواء الاحتراق", 80),
            new("DET-FLAME", "HS", "Burner Flame Scanner", "ماسح لهب الشعلة", 100),
        }),
        new("T", "Storage Tank", "خزان", "STAT", "TKF:22,JTY:2,*:1", false, new EquipTemplate[]
        {
            new("TANK-ST", "T", "Storage Tank", "خزان تخزين", 100),
            new("MIXER", "TM", "Tank Mixer", "خلاط خزان", 60),
            new("TX-LEVEL", "TLT", "Tank Level Transmitter", "مرسل مستوى خزان", 95),
            new("PVRV", "TBV", "Breather Valve", "صمام تنفس", 90),
        }),
        new("PSV", "Relief Valve", "صمام أمان", "STAT", "*:10", false, new EquipTemplate[]
        {
            new("PSV", "PSV", "Pressure Safety Valve", "صمام أمان", 100),
        }),
        new("FIC", "Flow Control Loop", "دائرة تحكم في التدفق", "INST", "*:10", false, new EquipTemplate[]
        {
            new("TX-FLOW", "FT", "Flow Transmitter", "مرسل تدفق", 100),
            new("FE-ORIF", "FE", "Orifice Plate", "لوح فوهة", 85),
            new("CV", "FV", "Flow Control Valve", "صمام تحكم في التدفق", 100),
            new("POSNR", "FY", "Valve Positioner", "موضع صمام", 85),
        }),
        new("PIC", "Pressure Control Loop", "دائرة تحكم في الضغط", "INST", "*:8", false, new EquipTemplate[]
        {
            new("TX-PRESS", "PT", "Pressure Transmitter", "مرسل ضغط", 100),
            new("CV", "PV", "Pressure Control Valve", "صمام تحكم في الضغط", 80),
            new("POSNR", "PY", "Valve Positioner", "موضع صمام", 65),
        }),
        new("TIC", "Temperature Loop", "دائرة قياس الحرارة", "INST", "*:8", false, new EquipTemplate[]
        {
            new("TX-TEMP", "TT", "Temperature Transmitter", "مرسل حرارة", 100),
            new("TE-TC", "TE", "Thermocouple", "مزدوج حراري", 95),
            new("TWELL", "TW", "Thermowell", "غمد حراري", 85),
        }),
        new("LIC", "Level Control Loop", "دائرة تحكم في المستوى", "INST", "*:6", false, new EquipTemplate[]
        {
            new("TX-LEVEL", "LT", "Level Transmitter", "مرسل مستوى", 100),
            new("CV", "LV", "Level Control Valve", "صمام تحكم في المستوى", 85),
            new("POSNR", "LY", "Valve Positioner", "موضع صمام", 70),
        }),
        new("XV", "Shutdown Valve", "صمام إيقاف", "INST", "*:5", false, new EquipTemplate[]
        {
            new("ESDV", "XV", "Emergency Shutdown Valve", "صمام إيقاف طارئ", 100),
            new("ACT-PN", "XA", "Pneumatic Actuator", "مشغل هوائي", 100),
            new("SOLENOID", "XY", "Solenoid Valve", "صمام ملف لولبي", 100),
            new("LIMIT-SW", "ZS", "Limit Switch", "مفتاح حدي", 90),
        }),
        new("GD", "Gas Detector", "كاشف غاز", "INST", "*:5", false, new EquipTemplate[]
        {
            new("DET-GAS", "GD", "Gas Detector", "كاشف غاز", 100),
        }),
        new("FD", "Flame Detector", "كاشف لهب", "INST", "*:3", false, new EquipTemplate[]
        {
            new("DET-FLAME", "FD", "Flame Detector", "كاشف لهب", 100),
        }),
        new("AT", "Analyzer", "محلل", "INST", "*:1", false, new EquipTemplate[]
        {
            new("ANALYZER", "AT", "Process Analyzer", "محلل عمليات", 100),
            new("SAMPLE-SYS", "AS", "Sample Conditioning System", "نظام تهيئة العينة", 90),
        }),
        new("MOV", "Motor Operated Valve", "صمام بمحرك", "MECH", "*:4", false, new EquipTemplate[]
        {
            new("VALVE-MO", "MOV", "Motor Operated Valve", "صمام بمحرك", 100),
            new("ACT-EL", "MA", "Electric Actuator", "مشغل كهربائي", 100),
        }),
        new("SIS", "Safety Logic Solver", "نظام منطق السلامة", "INST", "*:1", false, new EquipTemplate[]
        {
            new("LOGIC-SIS", "SIS", "Safety Logic Solver", "نظام منطق السلامة", 100),
        }),
        new("TR", "Transformer", "محول", "ELEC", "UTL:6", false, new EquipTemplate[]
        {
            new("TRANSF", "TR", "Power Transformer", "محول قدرة", 100),
            new("RELAY", "TRR", "Transformer Protection Relay", "مرحل حماية المحول", 90),
        }),
        new("SG", "Switchgear", "لوحة مفاتيح", "ELEC", "UTL:6", false, new EquipTemplate[]
        {
            new("SWGR", "SG", "Switchgear Panel", "لوحة مفاتيح كهربائية", 100),
            new("RELAY", "SR", "Protection Relay", "مرحل حماية", 90),
            new("BREAKER", "SB", "Circuit Breaker", "قاطع دائرة", 100),
        }),
        new("B", "Boiler", "غلاية", "STAT", "UTL:2", false, new EquipTemplate[]
        {
            new("BOILER", "B", "Steam Boiler", "غلاية بخار", 100),
            new("BURNER", "BB", "Boiler Burner", "شعلة الغلاية", 100),
            new("FAN", "BF", "Forced Draft Fan", "مروحة سحب قسري", 100),
            new("PSV", "BRV", "Boiler Safety Valve", "صمام أمان الغلاية", 100),
        }),
        new("FP", "Fire Water Pump", "طلمبة مياه حريق", "MECH", "UTL:2,TKF:2,JTY:2", false, new EquipTemplate[]
        {
            new("PUMP-FW", "FP", "Fire Water Pump", "طلمبة مياه حريق", 100),
            new("DRV-DSL", "FPE", "Diesel Engine Driver", "محرك ديزل", 100),
            new("PANEL-FP", "FPC", "Fire Pump Controller", "لوحة تحكم طلمبة الحريق", 100),
        }),
        new("DV", "Deluge Valve", "صمام غمر", "STAT", "UTL:2,TKF:8,JTY:6", false, new EquipTemplate[]
        {
            new("DELUGE", "DV", "Deluge Valve", "صمام غمر", 100),
            new("SOLENOID", "DVY", "Deluge Solenoid Valve", "صمام ملف لولبي للغمر", 90),
        }),
        new("LA", "Loading Arm", "ذراع تحميل", "MECH", "JTY:8", false, new EquipTemplate[]
        {
            new("LOADARM", "LA", "Marine Loading Arm", "ذراع تحميل بحري", 100),
            new("ERC", "LAC", "Emergency Release Coupling", "قارنة فصل طارئ", 100),
            new("HPU", "LAH", "Hydraulic Power Unit", "وحدة قدرة هيدروليكية", 80),
        }),
    };

    public static readonly PositionType[] BuildingPositionTypes =
    {
        new("AHU", "Air Handling Unit", "وحدة مناولة هواء", "MECH", "BLD:5", false, new EquipTemplate[]
        {
            new("FAN", "AHF", "Air Handling Unit Fan", "مروحة وحدة مناولة الهواء", 100),
            new("MOTOR-LV", "AHM", "Fan Motor", "محرك مروحة", 100),
        }),
        new("UPS", "UPS", "مزود طاقة غير منقطع", "ELEC", "BLD:2", false, new EquipTemplate[]
        {
            new("UPS", "UPS", "Uninterruptible Power Supply", "مزود طاقة غير منقطع", 100),
        }),
        new("DB", "Distribution Board", "لوحة توزيع", "ELEC", "BLD:3", false, new EquipTemplate[]
        {
            new("SWGR", "DB", "Distribution Board", "لوحة توزيع", 100),
        }),
    };

    /// <summary>
    /// Object types a product default map is expected to know. The class names are
    /// indicative only; the release-1 class seed (task T2) is the authority.
    /// </summary>
    public static readonly ObjectTypeInfo[] MappedObjectTypes =
    {
        new("PUMP-CF", "Centrifugal pump", "Rotating"),
        new("PUMP-RC", "Reciprocating pump", "Rotating"),
        new("PUMP-FW", "Fire water pump", "Safety and control"),
        new("COMP-CF", "Centrifugal compressor", "Rotating"),
        new("MOTOR-LV", "Electric motor (LV)", "Rotating"),
        new("MOTOR-HV", "Electric motor (HV)", "Rotating"),
        new("DRV-DSL", "Combustion engine", "Rotating"),
        new("FAN", "Fan / blower", "Rotating"),
        new("MIXER", "Mixer", "Rotating"),
        new("GEARBOX", "Gearbox", "Rotating"),
        new("COUPLING", "Coupling", "Rotating"),
        new("MSEAL", "Mechanical seal", "Rotating"),
        new("HX-ST", "Shell and tube heat exchanger", "Mechanical"),
        new("HX-AC", "Air cooled heat exchanger", "Mechanical"),
        new("VESSEL", "Pressure vessel", "Mechanical"),
        new("COLUMN", "Column", "Mechanical"),
        new("HEATER", "Fired heater", "Mechanical"),
        new("BOILER", "Boiler", "Mechanical"),
        new("BURNER", "Burner", "Mechanical"),
        new("TANK-ST", "Storage tank", "Mechanical"),
        new("FILTER", "Filter / strainer", "Mechanical"),
        new("DAMPENER", "Pulsation dampener", "Mechanical"),
        new("LOADARM", "Loading arm", "Mechanical"),
        new("HPU", "Hydraulic power unit", "Mechanical"),
        new("VALVE-MO", "Motor operated valve", "Mechanical"),
        new("ACT-EL", "Electric actuator", "Mechanical"),
        new("ACT-PN", "Pneumatic actuator", "Mechanical"),
        new("TRANSF", "Power transformer", "Electrical"),
        new("SWGR", "Switchgear", "Electrical"),
        new("BREAKER", "Circuit breaker", "Electrical"),
        new("RELAY", "Protection relay", "Electrical"),
        new("UPS", "Uninterruptible power supply", "Electrical"),
        new("PSV", "Pressure safety valve", "Safety and control"),
        new("PVRV", "Pressure/vacuum relief valve", "Safety and control"),
        new("ESDV", "Emergency shutdown valve", "Safety and control"),
        new("ERC", "Emergency release coupling", "Safety and control"),
        new("DELUGE", "Deluge valve", "Safety and control"),
        new("PANEL-FP", "Fire pump controller", "Safety and control"),
        new("DET-GAS", "Gas detector", "Safety and control"),
        new("DET-FLAME", "Flame detector", "Safety and control"),
        new("LOGIC-SIS", "Safety logic solver", "Safety and control"),
        new("SOLENOID", "Solenoid valve", "Safety and control"),
        new("LIMIT-SW", "Limit switch", "Safety and control"),
        new("VIB-SW", "Vibration switch", "Safety and control"),
        new("CV", "Control valve", "Safety and control"),
        new("POSNR", "Valve positioner", "Safety and control"),
        new("TX-FLOW", "Flow transmitter", "Safety and control"),
        new("TX-PRESS", "Pressure transmitter", "Safety and control"),
        new("TX-TEMP", "Temperature transmitter", "Safety and control"),
        new("TX-LEVEL", "Level transmitter", "Safety and control"),
        new("TE-TC", "Temperature element", "Safety and control"),
        new("TWELL", "Thermowell", "Safety and control"),
        new("FE-ORIF", "Flow element", "Safety and control"),
        new("GAUGE-P", "Pressure gauge", "Safety and control"),
        new("GAUGE-T", "Temperature gauge", "Safety and control"),
        new("GAUGE-L", "Level gauge", "Safety and control"),
        new("ANALYZER", "Process analyzer", "Safety and control"),
        new("SAMPLE-SYS", "Sample system", "Safety and control"),
    };

    /// <summary>Legacy or catch-all codes no default map should recognise.</summary>
    public static readonly string[] UnmappedObjectTypes =
    {
        "Z001", "Z002", "Z-MISC", "OTHER", "GEN", "UNKN",
        "ZINST9", "X-TEMP", "LEGACY", "SPARE", "ZZ-OLD", "TOOLS",
    };

    public static readonly string[] Manufacturers =
    {
        "Hallvane", "Orrex", "Kestrow", "Mervatek", "Sudrin", "Nordrel", "Tavix",
        "Quorvane", "Elmastra", "Zeffiron", "Brimholt", "Vantrell", "Dunmora", "Ilkaris",
    };

    public static readonly string[] ShortDescriptions = { "-", "NA", "TBD", "XX", "?", "N/A" };

    /// <summary>Throws when the catalog is internally inconsistent.</summary>
    public static void Validate()
    {
        var mapped = MappedObjectTypes.Select(o => o.Code).ToHashSet(StringComparer.Ordinal);
        if (mapped.Count != MappedObjectTypes.Length)
        {
            throw new InvalidOperationException("Duplicate code in MappedObjectTypes.");
        }

        var tagPrefixes = new HashSet<string>(StringComparer.Ordinal);
        var positionPrefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (PositionType type in ProcessPositionTypes.Concat(BuildingPositionTypes))
        {
            if (!positionPrefixes.Add(type.Prefix))
            {
                throw new InvalidOperationException($"Duplicate position prefix '{type.Prefix}'.");
            }

            if (type.Items.Length == 0 || type.Items[0].Percent != 100)
            {
                throw new InvalidOperationException($"Position type '{type.Prefix}' needs a main item.");
            }

            foreach (EquipTemplate item in type.Items)
            {
                if (!tagPrefixes.Add(item.TagPrefix))
                {
                    throw new InvalidOperationException($"Duplicate tag prefix '{item.TagPrefix}'.");
                }

                if (!mapped.Contains(item.ObjectType))
                {
                    throw new InvalidOperationException($"Object type '{item.ObjectType}' is not in the mapped list.");
                }
            }
        }

        foreach (string code in UnmappedObjectTypes)
        {
            if (mapped.Contains(code))
            {
                throw new InvalidOperationException($"'{code}' is listed as both mapped and unmapped.");
            }
        }
    }
}
