namespace PdfStudio.Domain.Interfaces;

/// <summary>
/// ライセンス管理サービスのインターフェース。
/// 商用化フェーズで実装を差し替えることを前提とした素地。
/// MVP段階ではすべて Free 扱い・全機能解放のNullObject実装で十分。
/// </summary>
public interface ILicenseService
{
    LicenseTier CurrentTier { get; }

    bool IsFeatureAvailable(FeatureFlag feature);

    Task<LicenseValidationResult> ValidateAsync(
        string licenseKey,
        CancellationToken ct = default);
}

public enum LicenseTier
{
    Free,
    Personal,
    Professional,
    Enterprise
}

[Flags]
public enum FeatureFlag
{
    None              = 0,
    BasicViewing      = 1 << 0,
    BasicEditing      = 1 << 1,
    Annotations       = 1 << 2,
    Ocr               = 1 << 3,
    DigitalSignature  = 1 << 4,
    BatchProcessing   = 1 << 5,
    CloudSync         = 1 << 6,
    All = BasicViewing | BasicEditing | Annotations | Ocr |
          DigitalSignature | BatchProcessing | CloudSync
}

public record LicenseValidationResult(
    bool IsValid,
    LicenseTier Tier,
    DateTime? ExpiresAt,
    string? Message);
