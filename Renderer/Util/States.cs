using SharpDX.Direct3D11;
using Tiger.Schema;

namespace Charm.Renderer;

public enum DepthMode
{
    // default
    Reverse,
    // used for shadow maps and decals
    Forward,
}

public class States
{
    private static readonly List<Comparison> REVERSED_DEPTH_FUNCS = new List<Comparison>()
    {
        Comparison.Always,
        Comparison.Always,
        Comparison.LessEqual,
        Comparison.LessEqual,
        Comparison.GreaterEqual,
        Comparison.GreaterEqual,
        Comparison.Greater,
        Comparison.LessEqual,
        Comparison.LessEqual,
        Comparison.Always,
        Comparison.Never,
        Comparison.Always,
        Comparison.LessEqual,
        Comparison.LessEqual,
    };

    public StateSelection CurrentState { get; set; }
    public StateSelection DefaultState { get; set; }

    private Dictionary<(int, int), RasterizerState> _rasStates = new();
    private Dictionary<int, (DepthStencilState reverse, DepthStencilState forward)> _depthStencilStates = new();
    private Dictionary<int, BlendState> _blendStates = new();

    public (int, int) CurrentRasState;
    public int CurrentDepthState;
    public int CurrentBlendState;
    public int CurrentStencilRef = 0;
    private DepthMode _depthMode = DepthMode.Reverse;

    public void ResetStates()
    {
        CurrentRasState = (-1, -1);
        CurrentDepthState = -1;
        CurrentBlendState = -1;
    }


    /// <summary>
    /// Sets the *default* StateSelection, should be used before any pipeline or render pass as this is what gets combined with any unset states from MaterialData.
    /// See MaterialData Bind()
    /// </summary>
    /// <param name="context"></param>
    /// <param name="state"></param>
    public void SetDefaultState(DeviceContext context, StateSelection state)
    {
        DefaultState = state;
        SetState(context, state);
    }

    /// <summary>
    /// Sets the given StateSelection, should only be used to directly override the current state.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="state"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public void SetState(DeviceContext context, StateSelection state)
    {
        if (state.Raw() == CurrentState.Raw())
            return;

        ResetStates();
        CurrentState = state;

        context.Rasterizer.State = CreateRasterizerState(context, state.RasterizerState(), state.DepthBiasState()) ?? throw new ArgumentNullException();
        context.OutputMerger.DepthStencilState = CreateDepthStencilState(context, state.DepthStencilState()) ?? throw new ArgumentNullException();
        context.OutputMerger.BlendState = CreateBlendState(context, state.BlendState()) ?? throw new ArgumentNullException();
    }

    public void SetDepthMode(DeviceContext context, DepthMode mode)
    {
        if (_depthMode != mode)
        {
            _depthMode = mode;
            int d = CurrentDepthState;
            CreateDepthStencilState(context, d);

            CurrentDepthState = -1;
            SetDepthStencilState(context, d);
        }
    }

    public void SetStencilRef(DeviceContext context, int stencilRef)
    {
        if (CurrentStencilRef != stencilRef)
        {
            CurrentStencilRef = stencilRef;
            int d = CurrentDepthState;
            CreateDepthStencilState(context, d);

            CurrentDepthState = -1;
            SetDepthStencilState(context, d);
        }
    }

    private void SetDepthStencilState(DeviceContext context, int index)
    {
        if (CurrentDepthState != index)
        {
            CurrentDepthState = index;
            context.OutputMerger.SetDepthStencilState(_depthMode == DepthMode.Reverse ? _depthStencilStates[index].reverse : _depthStencilStates[index].forward, CurrentStencilRef);
        }
    }

    private RasterizerState CreateRasterizerState(DeviceContext context, int rast, int depthBias)
    {
        if (rast == -1)
            return null;

        CurrentRasState = (rast, depthBias);
        if (_rasStates.TryGetValue((rast, depthBias), out var value))
            return value;

        RenderStates.BungieRasterizerDesc rasState = RenderStates.RasterizerStates[rast];

        var state = new RasterizerStateDescription
        {
            CullMode = rasState.CullMode,
            FillMode = rasState.FillMode,
            IsDepthClipEnabled = rasState.DepthClipEnable,
            IsFrontCounterClockwise = rasState.FrontCounterClockwise,
        };

        if (depthBias != -1)
        {
            RenderStates.BungieDepthBiasDesc depthState = RenderStates.DepthBiasStates[depthBias];
            state.DepthBias = depthState.DepthBias;
            state.SlopeScaledDepthBias = depthState.SlopeScaledDepthBias;
            state.DepthBiasClamp = depthState.DepthBiasClamp;
            state.IsScissorEnabled = rasState.ScissorEnable;
        }

        var rasterizerState = new RasterizerState(context.Device, state);
        rasterizerState.DebugName = $"RasterizerState_{rast}_{depthBias}";
        _rasStates.TryAdd((rast, depthBias), rasterizerState);

        return rasterizerState;
    }

    private DepthStencilState CreateDepthStencilState(DeviceContext context, int state)
    {
        if (state == -1)
            return null;

        CurrentDepthState = state;
        if (_depthStencilStates.TryGetValue(state, out var value))
            return _depthMode == DepthMode.Reverse ? value.reverse : value.forward;

        RenderStates.BungieDepthStencilDesc dsState = RenderStates.DepthStencilStates[state];
        int _depthIndex = RenderStates.DEPTH_STENCIL_COMBOS[state].Item1;

        var func = new DepthStencilStateDescription
        {
            IsDepthEnabled = dsState.Depth.Enable,
            DepthWriteMask = (DepthWriteMask)dsState.Depth.WriteMask,
            DepthComparison = dsState.Depth.Func,
            IsStencilEnabled = dsState.Stencil.StencilEnable,
            StencilReadMask = (byte)dsState.Stencil.StencilReadMask,
            StencilWriteMask = (byte)dsState.Stencil.StencilWriteMask,
            FrontFace = new DepthStencilOperationDescription
            {
                Comparison = dsState.Stencil.FrontFace.Func,
                DepthFailOperation = dsState.Stencil.FrontFace.DepthFailOp,
                FailOperation = dsState.Stencil.FrontFace.FailOp,
                PassOperation = dsState.Stencil.FrontFace.PassOp
            },
            BackFace = new DepthStencilOperationDescription
            {
                Comparison = dsState.Stencil.BackFace.Func,
                DepthFailOperation = dsState.Stencil.BackFace.DepthFailOp,
                FailOperation = dsState.Stencil.BackFace.FailOp,
                PassOperation = dsState.Stencil.BackFace.PassOp
            }
        };

        var regular = new DepthStencilState(context.Device, func);

        func.DepthComparison = REVERSED_DEPTH_FUNCS[_depthIndex];
        var regular_reversed = new DepthStencilState(context.Device, func);

        regular.DebugName = $"DepthStencilState_{state}";
        regular_reversed.DebugName = $"DepthStencilState_{state}_Reverse";

        _depthStencilStates.TryAdd(state, (regular, regular_reversed));
        return _depthMode == DepthMode.Reverse ? regular : regular_reversed;
    }

    private BlendState CreateBlendState(DeviceContext context, int state)
    {
        if (state == -1)
            return null;

        CurrentBlendState = state;
        if (_blendStates.TryGetValue(state, out var value))
            return value;

        RenderStates.BungieBlendDesc blendState = RenderStates.BlendStates[state];

        BlendStateDescription blendStateDescription = default(BlendStateDescription);
        blendStateDescription.AlphaToCoverageEnable = blendState.AlphaToCoverageEnable;
        blendStateDescription.IndependentBlendEnable = blendState.IndependentBlendEnable;
        blendStateDescription.RenderTarget[0] = blendState.BlendDesc[0];
        blendStateDescription.RenderTarget[1] = blendState.BlendDesc[1];
        blendStateDescription.RenderTarget[2] = blendState.BlendDesc[2];
        blendStateDescription.RenderTarget[3] = blendState.BlendDesc[3];

        var blend = new BlendState(context.Device, blendStateDescription);
        blend.DebugName = $"BlendState_{state}";
        _blendStates.TryAdd(state, blend);

        return blend;
    }

    public void DisposeStates()
    {
        foreach (var state in _rasStates.Values)
            state?.Dispose();
        _rasStates.Clear();

        foreach (var state in _depthStencilStates.Values)
        {
            state.forward?.Dispose();
            state.reverse?.Dispose();
        }
        _depthStencilStates.Clear();

        foreach (var state in _blendStates.Values)
            state?.Dispose();
        _blendStates.Clear();
    }
}


