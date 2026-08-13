namespace DesktopBuddy.AssetForge.Core;

public sealed record AuthoringTemplateSpec(string Id, AssetFamily Family, string DisplayName, string TemplateFileName, string Summary, IReadOnlyList<string> Guides, bool Implemented);

public static class AuthoringTemplateCatalog
{
    public const string GlassesId = "buddy.glasses";
    public const string TorsoId = "buddy.torso";
    public const string FeetId = "buddy.feet";

    public static IReadOnlyList<AuthoringTemplateSpec> All { get; } =
    [
        new(GlassesId, AssetFamily.BuddyStudio, "Glasses", "desktop_buddy_glasses_template_1024.png", "Buddy-head placement guide for front frames, bridge and temple roots.", ["Buddy head silhouette", "Eye line", "Eye centres", "Face centre line", "Recommended frame envelope", "Temple-root regions"], true),
        new("buddy.hair", AssetFamily.BuddyStudio, "Hair", "desktop_buddy_hair_template_1024.png", "Head/scalp guide for hair silhouettes and attachment coverage.", ["Buddy head silhouette", "Scalp envelope", "Hairline guide", "Face keep-out region", "Centre line", "Attachment envelope"], false),
        new("buddy.headwear", AssetFamily.BuddyStudio, "Headwear", "desktop_buddy_headwear_template_1024.png", "Head guide with crown contact and safe placement regions.", ["Buddy head silhouette", "Crown contact band", "Centre line", "Recommended bounds", "Face keep-out region"], false),
        new("buddy.accessory", AssetFamily.BuddyStudio, "Accessory", "desktop_buddy_accessory_template_1024.png", "Head/face guide for authored accessory placement around known attachment regions.", ["Buddy head silhouette", "Face centre", "Eye line", "Ear-side regions", "Recommended attachment regions"], false),
        new(TorsoId, AssetFamily.BuddyStudio, "Top / Torso replacement", "desktop_buddy_torso_template_1024.png", "Torso replacement coloring guide aligned to the trusted Buddy visual rig.", ["Default torso silhouette", "Centre line", "Neck/shoulder connector region", "Lower connector region", "Recommended envelope", "Translucent physics envelope"], true),
        new(FeetId, AssetFamily.BuddyStudio, "Shoes / Foot replacement", "desktop_buddy_foot_template_1024.png", "Single-foot replacement guide; the paired counterpart is generated deterministically.", ["Default foot silhouette", "Ankle connector", "Forward direction", "Ground line", "Recommended envelope", "Translucent physics envelope"], true),
        new("environment.lamp", AssetFamily.Environment, "Lamp", "desktop_buddy_lamp_template_1024.png", "Floor-anchored lamp guide defining base contact, body envelope and authored light region.", ["Floor line", "Bottom-centre base contact zone", "Vertical centre line", "Object safe bounds", "Shade envelope", "Light-source/emitter region", "Buddy scale reference"], false),
        new("environment.sofa", AssetFamily.Environment, "Sofa", "desktop_buddy_sofa_template_1024.png", "Floor-anchored furniture guide with seat/back proportions and Buddy scale reference.", ["Floor line", "Base contact zone", "Centre line", "Seat-height guide", "Back envelope", "Object safe bounds", "Buddy scale reference"], false),
        new("environment.table", AssetFamily.Environment, "Table", "desktop_buddy_table_template_1024.png", "Floor-anchored table guide with authored support and tabletop regions.", ["Floor line", "Base/leg contact region", "Centre line", "Tabletop height", "Tabletop envelope", "Object safe bounds", "Buddy scale reference"], false),
        new("environment.plant", AssetFamily.Environment, "Plant", "desktop_buddy_plant_template_1024.png", "Floor-anchored plant guide separating pot/base contact from foliage volume.", ["Floor line", "Pot/base contact zone", "Centre line", "Pot envelope", "Foliage safe bounds", "Buddy scale reference"], false),
        new("environment.painting", AssetFamily.Environment, "Painting", "desktop_buddy_painting_template_1024.png", "Wall-anchored guide for framed/decorative wall art.", ["Wall plane", "Wall anchor centre", "Horizontal/vertical centre lines", "Recommended art bounds", "Frame-safe margin", "Buddy scale reference"], false),
    ];

    public static AuthoringTemplateSpec Glasses => Get(GlassesId);
    public static AuthoringTemplateSpec Get(string id) => All.FirstOrDefault(spec => string.Equals(spec.Id, id, StringComparison.Ordinal)) ?? throw new KeyNotFoundException($"Unknown Asset Forge template category '{id}'.");

    public static byte[] CreatePng(string id)
    {
        AuthoringTemplateSpec spec = Get(id);
        if (!spec.Implemented) throw new NotSupportedException($"{spec.DisplayName} template generation is planned but not implemented yet.");
        return id switch
        {
            GlassesId => BuddyHeadTemplateGenerator.CreatePng(),
            TorsoId => PartReplacementTemplateGenerator.CreateTorsoPng(),
            FeetId => PartReplacementTemplateGenerator.CreateFootPng(),
            _ => throw new NotSupportedException($"No template generator is registered for '{id}'."),
        };
    }
}
