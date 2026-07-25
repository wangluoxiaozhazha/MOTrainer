namespace MOTrainer.Core.Features;

public enum DelayedBuildFeature
{
    UnitUnlimited,
    QueueLimit1000,
}

public sealed record DelayedFeatureStatus(
    bool Desired,
    FeatureState State,
    string Message);

public sealed record DelayedBuildPolicySnapshot(
    DelayedFeatureStatus UnitUnlimited,
    DelayedFeatureStatus QueueLimit1000);
