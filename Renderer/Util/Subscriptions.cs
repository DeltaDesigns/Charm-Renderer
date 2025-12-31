using Tiger;

namespace Charm.Renderer;

[Flags]
public enum FeatureRendererSubscription : uint
{
	None = 0,

	StaticObjects = 1u << TfxFeatureRenderer.StaticObjects,
	DynamicObjects = 1u << TfxFeatureRenderer.DynamicObjects,
	ExampleEntity = 1u << TfxFeatureRenderer.ExampleEntity,
	SkinnedObject = 1u << TfxFeatureRenderer.SkinnedObject,
	Gear = 1u << TfxFeatureRenderer.Gear,
	RigidObject = 1u << TfxFeatureRenderer.RigidObject,
	Cloth = 1u << TfxFeatureRenderer.Cloth,
	ChunkedInstanceObjects = 1u << TfxFeatureRenderer.ChunkedInstanceObjects,
	SoftDeformable = 1u << TfxFeatureRenderer.SoftDeformable,
	TerrainPatch = 1u << TfxFeatureRenderer.TerrainPatch,
	SpeedtreeTrees = 1u << TfxFeatureRenderer.SpeedtreeTrees,
	EditorTerrainTile = 1u << TfxFeatureRenderer.EditorTerrainTile,
	EditorMesh = 1u << TfxFeatureRenderer.EditorMesh,
	BatchedEditorMesh = 1u << TfxFeatureRenderer.BatchedEditorMesh,
	EditorDecal = 1u << TfxFeatureRenderer.EditorDecal,
	Particles = 1u << TfxFeatureRenderer.Particles,
	ChunkedLights = 1u << TfxFeatureRenderer.ChunkedLights,
	DeferredLights = 1u << TfxFeatureRenderer.DeferredLights,
	SkyTransparent = 1u << TfxFeatureRenderer.SkyTransparent,
	Widget = 1u << TfxFeatureRenderer.Widget,
	Decals = 1u << TfxFeatureRenderer.Decals,
	DynamicDecals = 1u << TfxFeatureRenderer.DynamicDecals,
	RoadDecals = 1u << TfxFeatureRenderer.RoadDecals,
	Water = 1u << TfxFeatureRenderer.Water,
	LensFlares = 1u << TfxFeatureRenderer.LensFlares,
	Volumetrics = 1u << TfxFeatureRenderer.Volumetrics,
	Cubemaps = 1u << TfxFeatureRenderer.Cubemaps,

	All = uint.MaxValue
}

public static class FeatureRendererSubscriptionExtensions
{
	public static FeatureRendererSubscription AllBut(TfxFeatureRenderer feature)
	{
		var bit = (FeatureRendererSubscription)(1u << (int)feature);
		return FeatureRendererSubscription.All & ~bit;
	}

	public static bool IsSubscribed(this FeatureRendererSubscription subscription, TfxFeatureRenderer feature)
	{
		var bit = (FeatureRendererSubscription)(1u << (int)feature);
		return (subscription & bit) != 0;
	}
}

[Flags]
public enum RenderStageSubscription : uint
{
	None = 0,

	GenerateGbuffer = 1u << TfxRenderStage.GenerateGbuffer,
	Decals = 1u << TfxRenderStage.Decals,
	InvestmentDecals = 1u << TfxRenderStage.InvestmentDecals,
	ShadowGenerate = 1u << TfxRenderStage.ShadowGenerate,
	LightingApply = 1u << TfxRenderStage.LightingApply,
	LightProbeApply = 1u << TfxRenderStage.LightProbeApply,
	DecalsAdditive = 1u << TfxRenderStage.DecalsAdditive,
	Transparents = 1u << TfxRenderStage.Transparents,
	Distortion = 1u << TfxRenderStage.Distortion,
	LightShaftOcclusion = 1u << TfxRenderStage.LightShaftOcclusion,
	SkinPrepass = 1u << TfxRenderStage.SkinPrepass,
	LensFlares = 1u << TfxRenderStage.LensFlares,
	DepthPrepass = 1u << TfxRenderStage.DepthPrepass,
	WaterReflection = 1u << TfxRenderStage.WaterReflection,
	PostprocessTransparentStencil = 1u << TfxRenderStage.PostprocessTransparentStencil,
	Impulse = 1u << TfxRenderStage.Impulse,
	Reticle = 1u << TfxRenderStage.Reticle,
	WaterRipples = 1u << TfxRenderStage.WaterRipples,
	MaskSunLight = 1u << TfxRenderStage.MaskSunLight,
	Volumetrics = 1u << TfxRenderStage.Volumetrics,
	Cubemaps = 1u << TfxRenderStage.Cubemaps,
	PostprocessScreen = 1u << TfxRenderStage.PostprocessScreen,
	WorldForces = 1u << TfxRenderStage.WorldForces,
	ComputeSkinning = 1u << TfxRenderStage.ComputeSkinning,

	All = uint.MaxValue
}

public static class RenderStageSubscriptionExtensions
{
	public static RenderStageSubscription AllBut(TfxRenderStage stage)
	{
		var bit = (RenderStageSubscription)(1u << (int)stage);
		return RenderStageSubscription.All & ~bit;
	}

	public static bool IsSubscribed(this RenderStageSubscription subscription, TfxRenderStage stage)
	{
		var bit = (RenderStageSubscription)(1u << (int)stage);
		return (subscription & bit) != 0;
	}

	public static RenderStageSubscription FromPartRangeList(ushort[] partRanges)
	{
		RenderStageSubscription flags = RenderStageSubscription.None;
		for (int i = 0; i < 24; i++)
		{
			if (partRanges[i] != partRanges[i + 1])
			{
				flags |= (RenderStageSubscription)(1u << i);
			}
		}
		return flags;
	}

	public static RenderStageSubscription FromStages(IEnumerable<TfxRenderStage> stages)
	{
		RenderStageSubscription result = RenderStageSubscription.None;
		foreach (var stage in stages)
		{
			result |= (RenderStageSubscription)(1u << (int)stage);
		}
		return result;
	}

	public static RenderStageSubscription FromStage(TfxRenderStage stage)
	{
		return (RenderStageSubscription)(1u << (int)stage);
	}
}

