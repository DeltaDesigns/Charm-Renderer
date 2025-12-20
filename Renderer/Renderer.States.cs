using SharpDX.Direct3D11;
using Tiger.Schema;

namespace Charm.Renderer;

public partial class CharmRenderer
{
    public StateSelection CurrentState { get; set; }

    private Dictionary<(int, int), RasterizerState> _rasStates = new();
    private Dictionary<int, DepthStencilState> _depthStencilStates = new();
    private Dictionary<int, BlendState> _blendStates = new();

    public (int, int) CurrentRasState;
    public int CurrentDepthState;
    public int CurrentBlendState;
    public int CurrentStencilRef = 0;

    public void ResetStates()
    {
        CurrentRasState = (-1, -1);
        CurrentDepthState = -1;
        CurrentBlendState = -1;
    }

    public void CreateStates(StateSelection state)
    {
        if (state.Raw() == CurrentState.Raw())
            return;

        ResetStates();
        CurrentState = state;

        Context.Rasterizer.State = CreateRasterizerState(state.RasterizerState(), state.DepthBiasState()) ?? throw new ArgumentNullException();
        Context.OutputMerger.DepthStencilState = CreateDepthStencilState(state.DepthStencilState()) ?? throw new ArgumentNullException();
        Context.OutputMerger.BlendState = CreateBlendState(state.BlendState()) ?? throw new ArgumentNullException();
    }

    private void SetStencilRef(int stencilRef)
    {
        if (CurrentStencilRef != stencilRef)
        {
            CurrentStencilRef = stencilRef;
            int d = CurrentDepthState;
            CreateDepthStencilState(d);

            CurrentDepthState = -1;
            SetDepthStencilState(d);
        }
    }

    private void SetDepthStencilState(int index)
    {
        if (CurrentDepthState != index)
        {
            CurrentDepthState = index;
            Context.OutputMerger.SetDepthStencilState(_depthStencilStates[index], CurrentStencilRef);
        }
    }

    private RasterizerState CreateRasterizerState(int rast, int depthBias)
    {
        if (rast == -1)
            return null;

        CurrentRasState = (rast, depthBias);
        if (_rasStates.ContainsKey((rast, depthBias)))
            return _rasStates[((rast, depthBias))];

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

        var rasterizerState = new RasterizerState(Device, state);
        _rasStates.TryAdd((rast, depthBias), rasterizerState);

        return rasterizerState;
    }

    private DepthStencilState CreateDepthStencilState(int state)
    {
        if (state == -1)
            return null;

        CurrentDepthState = state;
        if (_depthStencilStates.ContainsKey(state))
            return _depthStencilStates[state];

        RenderStates.BungieDepthStencilDesc dsState = RenderStates.DepthStencilStates[state];

        var depthStencilState = new DepthStencilState(Device, new DepthStencilStateDescription
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
        });
        _depthStencilStates.TryAdd(state, depthStencilState);

        return depthStencilState;
    }

    private BlendState CreateBlendState(int state)
    {
        if (state == -1)
            return null;

        CurrentBlendState = state;
        if (_blendStates.ContainsKey(state))
            return _blendStates[state];

        RenderStates.BungieBlendDesc blendState = RenderStates.BlendStates[state];

        BlendStateDescription blendStateDescription = default(BlendStateDescription);
        blendStateDescription.AlphaToCoverageEnable = blendState.AlphaToCoverageEnable;
        blendStateDescription.IndependentBlendEnable = blendState.IndependentBlendEnable;
        BlendStateDescription result = blendStateDescription;
        RenderTargetBlendDescription[] renderTarget = result.RenderTarget;
        for (int i = 0; i < renderTarget.Length; i++)
        {
            renderTarget[i].IsBlendEnabled = blendState.BlendDesc.IsBlendEnabled;
            renderTarget[i].SourceBlend = blendState.BlendDesc.SourceBlend;
            renderTarget[i].DestinationBlend = blendState.BlendDesc.DestinationBlend;
            renderTarget[i].BlendOperation = blendState.BlendDesc.BlendOperation;
            renderTarget[i].SourceAlphaBlend = blendState.BlendDesc.SourceAlphaBlend;
            renderTarget[i].DestinationAlphaBlend = blendState.BlendDesc.DestinationAlphaBlend;
            renderTarget[i].AlphaBlendOperation = blendState.BlendDesc.AlphaBlendOperation;
            renderTarget[i].RenderTargetWriteMask = blendState.BlendDesc.RenderTargetWriteMask;
        }

        var blend = new BlendState(Device, result);
        _blendStates.TryAdd(state, blend);

        return blend;
    }

    public void DisposeStates()
    {
        foreach (var state in _rasStates.Values)
            state?.Dispose();
        _rasStates.Clear();

        foreach (var state in _depthStencilStates.Values)
            state?.Dispose();
        _depthStencilStates.Clear();

        foreach (var state in _blendStates.Values)
            state?.Dispose();
        _blendStates.Clear();
    }
}
