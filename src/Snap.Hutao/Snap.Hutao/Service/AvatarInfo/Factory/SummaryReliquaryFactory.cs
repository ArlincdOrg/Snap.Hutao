﻿// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Core.ExceptionService;
using Snap.Hutao.Model.Intrinsic;
using Snap.Hutao.Model.Metadata.Converter;
using Snap.Hutao.Model.Metadata.Reliquary;
using Snap.Hutao.Model.Primitive;
using Snap.Hutao.Service.AvatarInfo.Factory.Builder;
using Snap.Hutao.ViewModel.AvatarProperty;
using System.Runtime.InteropServices;
using MetadataReliquary = Snap.Hutao.Model.Metadata.Reliquary.Reliquary;
using ModelAvatarInfo = Snap.Hutao.Web.Enka.Model.AvatarInfo;

namespace Snap.Hutao.Service.AvatarInfo.Factory;

[HighQuality]
internal sealed class SummaryReliquaryFactory
{
    private readonly SummaryFactoryMetadataContext metadataContext;
    private readonly ModelAvatarInfo avatarInfo;
    private readonly Web.Enka.Model.Equip equip;

    public SummaryReliquaryFactory(SummaryFactoryMetadataContext metadataContext, ModelAvatarInfo avatarInfo, Web.Enka.Model.Equip equip)
    {
        this.metadataContext = metadataContext;
        this.avatarInfo = avatarInfo;
        this.equip = equip;
    }

    public static ReliquaryView Create(SummaryFactoryMetadataContext metadataContext, ModelAvatarInfo avatarInfo, Web.Enka.Model.Equip equip)
    {
        return new SummaryReliquaryFactory(metadataContext, avatarInfo, equip).Create();
    }

    public ReliquaryView Create()
    {
        MetadataReliquary reliquary = metadataContext.IdReliquaryMap[equip.ItemId];

        ArgumentNullException.ThrowIfNull(equip.Reliquary);
        List<ReliquarySubProperty> subProperties = equip.Reliquary.AppendPropIdList.EmptyIfNull().SelectList(CreateSubProperty);

        ReliquaryViewBuilder reliquaryViewBuilder = new ReliquaryViewBuilder()
            .SetName(reliquary.Name)
            .SetIcon(RelicIconConverter.IconNameToUri(reliquary.Icon))
            .SetDescription(reliquary.Description)
            .SetLevel($"+{equip.Reliquary.Level - 1U}")
            .SetQuality(reliquary.RankLevel);

        int affixCount = GetSecondaryAffixCount(reliquary, equip.Reliquary);

        if (subProperties.Count > 0)
        {
            reliquaryViewBuilder
                .SetPrimarySubProperties(subProperties.GetRange(..^affixCount))
                .SetSecondarySubProperties(subProperties.GetRange(^affixCount..))
                .SetComposedSubProperties(CreateComposedSubProperties(equip.Reliquary.AppendPropIdList));

            ReliquaryMainAffixLevel relicLevel = metadataContext.ReliquaryMainAffixLevels.Single(r => r.Level == equip.Reliquary.Level && r.Rank == reliquary.RankLevel);
            FightProperty property = metadataContext.IdReliquaryMainPropertyMap[equip.Reliquary.MainPropId];

            reliquaryViewBuilder
                .SetMainProperty(FightPropertyFormat.ToNameValue(property, relicLevel.PropertyMap[property]))
                .SetScore(ScoreReliquary(property, reliquary, relicLevel, subProperties));
        }

        return reliquaryViewBuilder.View;
    }

    private static int GetSecondaryAffixCount(MetadataReliquary metaReliquary, Web.Enka.Model.Reliquary enkaReliquary)
    {
        // 强化词条个数
        return (metaReliquary.RankLevel, enkaReliquary.Level.Value) switch
        {
            (QualityType.QUALITY_ORANGE, > 20U) => 5,
            (QualityType.QUALITY_ORANGE, > 16U) => 4,
            (QualityType.QUALITY_ORANGE, > 12U) => 3,
            (QualityType.QUALITY_ORANGE, > 08U) => 2,
            (QualityType.QUALITY_ORANGE, > 04U) => 1,
            (QualityType.QUALITY_ORANGE, _) => 0,

            (QualityType.QUALITY_PURPLE, > 16U) => 4,
            (QualityType.QUALITY_PURPLE, > 12U) => 3,
            (QualityType.QUALITY_PURPLE, > 08U) => 2,
            (QualityType.QUALITY_PURPLE, > 04U) => 1,
            (QualityType.QUALITY_PURPLE, _) => 0,

            (QualityType.QUALITY_BLUE, > 12U) => 3,
            (QualityType.QUALITY_BLUE, > 08U) => 2,
            (QualityType.QUALITY_BLUE, > 04U) => 1,
            (QualityType.QUALITY_BLUE, _) => 0,

            (QualityType.QUALITY_GREEN, > 04U) => 1,
            (QualityType.QUALITY_GREEN, _) => 0,

            (QualityType.QUALITY_WHITE, > 04U) => 1,
            (QualityType.QUALITY_WHITE, _) => 0,

            _ => 0,
        };
    }

    private List<ReliquaryComposedSubProperty> CreateComposedSubProperties(List<ReliquarySubAffixId> appendProps)
    {
        List<SummaryReliquarySubPropertyCompositionInfo> infos = [];
        foreach (ref readonly ReliquarySubAffixId subAffixId in CollectionsMarshal.AsSpan(appendProps))
        {
            ReliquarySubAffix subAffix = metadataContext.IdReliquarySubAffixMap[subAffixId];
            SummaryReliquarySubPropertyCompositionInfo info = infos.SingleOrAdd(prop => prop.Type == subAffix.Type, () => new(subAffix.Type));
            info.Count += 1;
            info.Value += subAffix.Value;
        }

        HutaoException.ThrowIf(infos.Count > 4, "无效的圣遗物数据");
        return infos.SelectList(info => info.ToReliquaryComposedSubProperty());
    }

    private float ScoreReliquary(FightProperty property, MetadataReliquary reliquary, ReliquaryMainAffixLevel relicLevel, List<ReliquarySubProperty> subProperties)
    {
        // 沙/杯/头
        // equip.Flat.EquipType is EquipType.EQUIP_SHOES or EquipType.EQUIP_RING or EquipType.EQUIP_DRESS
        if (equip.Flat.EquipType >= EquipType.EQUIP_SHOES)
        {
            // 从喵插件抓取的圣遗物评分权重
            // 部分复杂的角色暂时使用了默认值
            ReliquaryAffixWeight affixWeight = metadataContext.IdReliquaryAffixWeightMap.GetValueOrDefault(avatarInfo.AvatarId, ReliquaryAffixWeight.Default);
            ReliquaryMainAffixLevel? maxRelicLevel = metadataContext.ReliquaryMainAffixLevels.Where(r => r.Rank == reliquary.RankLevel).MaxBy(r => r.Level);
            ArgumentNullException.ThrowIfNull(maxRelicLevel);

            float percent = relicLevel.PropertyMap[property] / maxRelicLevel.PropertyMap[property];
            float baseScore = 8 * percent * affixWeight[property];

            float score = subProperties.Sum(p => p.Score);
            return ((score + baseScore) / 1700F) * 66F; // 加权平均
        }
        else
        {
            // 花/羽 的主属性确定
            float score = subProperties.Sum(p => p.Score);
            return (score / 900F) * 66F; // 加权平均
        }
    }

    [SuppressMessage("", "SH002")]
    private ReliquarySubProperty CreateSubProperty(ReliquarySubAffixId appendPropId)
    {
        ReliquarySubAffix affix = metadataContext.IdReliquarySubAffixMap[appendPropId];
        FightProperty property = affix.Type;

        return new(property, FightPropertyFormat.FormatValue(property, affix.Value), ScoreSubAffix(appendPropId));

        float ScoreSubAffix(in ReliquarySubAffixId appendId)
        {
            ReliquarySubAffix affix = metadataContext.IdReliquarySubAffixMap[appendId];

            ReliquaryAffixWeight affixWeight = metadataContext.IdReliquaryAffixWeightMap.GetValueOrDefault(avatarInfo.AvatarId, ReliquaryAffixWeight.Default);
            float weight = affixWeight[affix.Type] / 100F;

            // 数字词条，转换到等效百分比计算
            if (affix.Type is FightProperty.FIGHT_PROP_HP or FightProperty.FIGHT_PROP_ATTACK or FightProperty.FIGHT_PROP_DEFENSE)
            {
                // 等效百分比 [ 当前小字词条 / 角色基本属性 ]
                float equalPercent = affix.Value / avatarInfo.FightPropMap[affix.Type - 1];

                // 获取对应百分比词条权重
                weight = affixWeight[affix.Type + 1] / 100F;

                // 最大同属性百分比Id
                // 第四五位是战斗属性位
                // 小字的加成词条在十位加一后即变换为百分比词条
                ReliquarySubAffixId maxPercentAffixId = SummaryHelper.GetAffixMaxId(appendId + 10U);

                // 最大同属性百分比数值
                ReliquarySubAffix maxPercentAffix = metadataContext.IdReliquarySubAffixMap[maxPercentAffixId];
                HutaoException.ThrowIfNot(
                    maxPercentAffix.Type
                    is FightProperty.FIGHT_PROP_HP_PERCENT
                    or FightProperty.FIGHT_PROP_ATTACK_PERCENT
                    or FightProperty.FIGHT_PROP_DEFENSE_PERCENT,
                    "ReliquarySubAffix transform failed");
                float equalScore = equalPercent / maxPercentAffix.Value;

                return weight * equalScore * 100;
            }

            return weight * SummaryHelper.GetPercentSubAffixScore(appendId);
        }
    }
}