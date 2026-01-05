using HelixToolkit.Maths;
using SharpDX.Direct3D11;
using SharpDX.DirectInput;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Tiger.Schema;
using Buffer = SharpDX.Direct3D11.Buffer;

#if DEBUG
using TracyWrapper;
#endif

namespace Charm.Renderer;

public partial class CharmRenderer
{
	public UserDefinedAnnotation Annotation;

	public DirectInput Input = new DirectInput();
	public SharpDX.DirectInput.Keyboard Keyboard;
	public SharpDX.DirectInput.Mouse Mouse;

	public SharpDX.DirectInput.KeyboardState KeyboardState;
	public SharpDX.DirectInput.MouseState MouseState;

	public ConcurrentDictionary<Tiger.TfxScope, TfxScope> TfxScopes = new();

	private void CreateDefaults()
	{
		AssetManager = new AssetManager();

		CreateGrid();

		Annotation ??= Context.QueryInterface<UserDefinedAnnotation>();

		_blitVS ??= new VertexShader(Device, SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("renderer assets/shaders/blit.hlsl", "VSMain", "vs_5_0"));
		_blitPS ??= new PixelShader(Device, SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("renderer assets/shaders/blit.hlsl", "PSMain", "ps_5_0"));

		_clearAOVS ??= new VertexShader(Device, SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("renderer assets/shaders/clear_ao.hlsl", "VSMain", "vs_5_0"));
		_clearAOPS ??= new PixelShader(Device, SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("renderer assets/shaders/clear_ao.hlsl", "PSMainOne", "ps_5_0"));

		_fullHemiSkyTempVS ??= new VertexShader(Device, SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("renderer assets/shaders/full_hemi_sky_temp_vs.hlsl", "main", "vs_5_0"));
		_fullHemiSkyTempPS ??= new PixelShader(Device, SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("renderer assets/shaders/full_hemi_sky_temp_ps.hlsl", "main", "ps_5_0"));

		var vs = SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("renderer assets/shaders/debug_lines_vs.hlsl", "VSMain", "vs_5_0");
		_debugLinesVS ??= new VertexShader(Device, vs);
		_debugLinesPS ??= new PixelShader(Device, SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("renderer assets/shaders/debug_lines_ps.hlsl", "PSMain", "ps_5_0"));
		_debugLinesLayout ??= new InputLayout(Device, vs.Bytecode, RenderHelpers.GetInputLayout(0).ToArray());

		_debugPSCB ??= new Buffer(
			Device,
			SharpDX.Utilities.SizeOf<System.Numerics.Vector4>(),
			ResourceUsage.Default,
			BindFlags.ConstantBuffer,
			CpuAccessFlags.None,
			ResourceOptionFlags.None,
			0
		);

		_wireframeRS ??= new RasterizerState(Device, new RasterizerStateDescription
		{
			FillMode = FillMode.Wireframe,
			CullMode = CullMode.None,
			IsFrontCounterClockwise = false,
		});

		_pointSampler ??= new SamplerState(Device, new SamplerStateDescription
		{
			Filter = Filter.MinMagMipPoint,
			AddressU = TextureAddressMode.Clamp,
			AddressV = TextureAddressMode.Clamp,
			AddressW = TextureAddressMode.Clamp,
			ComparisonFunction = Comparison.Never,
			BorderColor = new(0.0f, 0.0f, 0.0f, 1.0f),
			MinimumLod = 0,
			MaximumLod = float.MaxValue,
		});
	}

	private void CreateScopes()
	{
		TfxScopes = new();
		foreach (var scope_global in Globals.Get().GetScopes())
		{
			TfxScopes.TryAdd(scope_global.Key, new TfxScope(scope_global.Value, Context));
		}
	}

	public void ExecutePipeline(string pipeline)
	{
		if (!_pipelineCache.ContainsKey(pipeline))
			_pipelineCache[pipeline] = new(Context, Globals.Get().GetPipeline(pipeline));

		var data = _pipelineCache[pipeline];

		data.Bind(Context);
	}

	public void UnbindAllRTVs()
	{
		Context.OutputMerger.SetRenderTargets(null, new RenderTargetView[8]);
	}

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

	public static bool IsAppFocused()
	{
		IntPtr foregroundWindow = GetForegroundWindow();

		if (foregroundWindow == IntPtr.Zero)
			return false;

		GetWindowThreadProcessId(foregroundWindow, out uint foregroundPid);
		return foregroundPid == (uint)Process.GetCurrentProcess().Id;
	}
}

public static class RenderHelpers
{
	public static AABB ComputeBoundingBox(IReadOnlyList<Tiger.Schema.Vector4> vertices)
	{
		Tiger.Schema.Vector4 min = new Tiger.Schema.Vector4(0);
		Tiger.Schema.Vector4 max = new Tiger.Schema.Vector4(0);

		if (vertices == null || vertices.Count == 0)
			return new AABB() { Min = min, Max = max };

		for (int i = 0; i < vertices.Count; i++)
		{
			Tiger.Schema.Vector4 v = vertices[i];

			if (v.X < min.X) min.X = v.X;
			if (v.Y < min.Y) min.Y = v.Y;
			if (v.Z < min.Z) min.Z = v.Z;

			if (v.X > max.X) max.X = v.X;
			if (v.Y > max.Y) max.Y = v.Y;
			if (v.Z > max.Z) max.Z = v.Z;
		}

		return new AABB() { Min = min, Max = max };
	}

	public static BoundingBox CreateFrom(this AABB aabb)
	{
		return new BoundingBox(
			new(aabb.Min.X, aabb.Min.Y, aabb.Min.Z),
			new(aabb.Max.X, aabb.Max.Y, aabb.Max.Z)
		);
	}

	public static BoundingBox CreateFrom(this AABB aabb,
		Tiger.Schema.Vector4 scale,
		Tiger.Schema.Vector4 trans)
	{
		var min = aabb.Min * scale - trans;
		var max = aabb.Max * scale + trans;
		return new BoundingBox(
			new(min.X, min.Y, min.Z),
			new(max.X, max.Y, max.Z)
		);
	}

	public static System.Numerics.Vector3[] GetBoundingBoxLines(BoundingBox box)
	{
		Vector3 min = box.Minimum;
		Vector3 max = box.Maximum;

		Vector3[] corners =
		{
			new(min.X, min.Y, min.Z),
			new(max.X, min.Y, min.Z),
			new(max.X, max.Y, min.Z),
			new(min.X, max.Y, min.Z),

			new(min.X, min.Y, max.Z),
			new(max.X, min.Y, max.Z),
			new(max.X, max.Y, max.Z),
			new(min.X, max.Y, max.Z)
		};

		return new System.Numerics.Vector3[]
		{
			// Bottom
			corners[0], corners[1],
			corners[1], corners[2],
			corners[2], corners[3],
			corners[3], corners[0],

			// Top
			corners[4], corners[5],
			corners[5], corners[6],
			corners[6], corners[7],
			corners[7], corners[4],

			// Vertical
			corners[0], corners[4],
			corners[1], corners[5],
			corners[2], corners[6],
			corners[3], corners[7],
		};
	}

	public static List<InputElement> GetInputLayout(int layoutIndex)
	{
		List<InputElement> inputs = new();
		var layout = Globals.Get().GetInputLayouts()[layoutIndex];
		foreach (var element in layout.Elements)
		{
			inputs.Add(new()
			{
				SemanticName = element.SemanticName,
				SemanticIndex = (int)element.SemanticIndex,
				Format = (SharpDX.DXGI.Format)element.Format,
				Slot = (int)element.BufferIndex,
				AlignedByteOffset = InputElement.AppendAligned,
				Classification = element.IsInstanceData ? InputClassification.PerInstanceData : InputClassification.PerVertexData,
			});
		}
		return inputs;
	}

	public static void Profile(string name)
	{
#if DEBUG
		Profiler.PushProfileZone(name);
#endif
	}

	public static void EndProfile()
	{
#if DEBUG
		Profiler.PopProfileZone();
#endif
	}
}

public class DyeMerger
{
	private readonly System.Numerics.Vector4[] _merged = new System.Numerics.Vector4[63];

	/// <summary>
	/// Merge the input evaluated arrays into the merged buffer.
	/// Null inputs are skipped.
	/// </summary>
	public void Merge(System.Numerics.Vector4[] buf0, System.Numerics.Vector4[] buf1, System.Numerics.Vector4[] buf2)
	{
		int index = 0;

		void Copy(System.Numerics.Vector4[] source)
		{
			if (source == null) return;
			int count = Math.Min(source.Length, _merged.Length - index);
			Array.Copy(source, 0, _merged, index, count);
			index += count;
		}

		Copy(buf0);
		Copy(buf1);
		Copy(buf2);

		// Fill remaining with zeros if any
		for (; index < _merged.Length; index++)
			_merged[index] = System.Numerics.Vector4.Zero;
	}

	/// <summary>
	/// Move an element from oldIndex to newIndex, shifting elements in between.
	/// </summary>
	public void Move(int oldIndex, int newIndex)
	{
		if (oldIndex == newIndex || oldIndex < 0 || oldIndex >= _merged.Length || newIndex < 0 || newIndex >= _merged.Length)
			return;

		System.Numerics.Vector4 temp = _merged[oldIndex];

		if (oldIndex < newIndex)
		{
			Array.Copy(_merged, oldIndex + 1, _merged, oldIndex, newIndex - oldIndex);
		}
		else
		{
			Array.Copy(_merged, newIndex, _merged, newIndex + 1, oldIndex - newIndex);
		}

		_merged[newIndex] = temp;
	}

	public System.Numerics.Vector4[] ToArray() => _merged;
}
