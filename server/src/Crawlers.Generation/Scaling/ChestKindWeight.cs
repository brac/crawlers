using Crawlers.Domain.Enums;

namespace Crawlers.Generation.Scaling;

/// <summary>
/// One entry in a per-floor chest-kind distribution. The placer rolls
/// each placed chest's kind from this weighted list independently. An
/// entry with weight 7 is seven times as likely to land as an entry
/// with weight 1; weights are integers to keep tuning intuitive.
/// </summary>
public sealed record ChestKindWeight(ChestKind Kind, int Weight);
