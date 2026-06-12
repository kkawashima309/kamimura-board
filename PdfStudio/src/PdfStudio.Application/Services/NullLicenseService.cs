using PdfStudio.Domain.Interfaces;

namespace PdfStudio.Application.Services;

/// <summary>
/// MVP用の常に全機能解放するライセンスサービス実装。
/// 商用化フェーズで本格的な実装に差し替える。
/// </summary>
public class NullLicenseService : ILicenseService
{
    public LicenseTier CurrentTier => LicenseTier.Free;

    public bool IsFeatureAvailable(FeatureFlag feature) => true;

    public Task<LicenseValidationResult> ValidateAsync(
        string licenseKey,
        CancellationToken ct = default)
    {
        return Task.FromResult(new LicenseValidationResult(
            IsValid: true,
            Tier: LicenseTier.Free,
            ExpiresAt: null,
            Message: "MVP版（全機能解放）"));
    }
}
