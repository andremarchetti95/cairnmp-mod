namespace CairnMultiplayerMod.GameApi;

/// <summary>Read-only native measurements for an opt-in partner catch.</summary>
internal readonly record struct BelayFallObservationSample(
    bool ContextValid,
    bool RouteReady,
    bool HasStation,
    bool Falling,
    bool Suspended,
    bool RopeTaut,
    bool PersonallyAnchored,
    long Rope,
    long Station,
    long LastPiton,
    uint Revision,
    float Height,
    float RopeLength,
    float Hp,
    float MaxHp,
    uint DamageCalls,
    float RequestedFallDamage,
    float ObservedFallHpLoss,
    float Slack = 0,
    float ReleasedRopeLength = float.NaN,
    float FixedPathLength = float.NaN,
    float ReleasedFinalLength = float.NaN);
