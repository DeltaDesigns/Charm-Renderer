using System.Runtime.CompilerServices;
using Arithmic;
using Charm.Renderer;
using HelixToolkit.Maths;
using SharpDX.Direct3D11;
using Tiger;
using Tiger.Schema;
using static TfxBytecodeOp;
using Vec4 = System.Numerics.Vector4;

public class TfxBytecodeInterpreter
{
    public TfxData[] Opcodes { get; }
    private Vec4[] _stack;
    private Vec4[] _temp;
    private Texture[] _pushTexCache = new Texture[32];
    public string Name { get; set; } = "";
    private int _sp; // Stack pointer
    private TfxData _curOp;
    public BytecodeType Type = BytecodeType.Expression;

    public TfxBytecodeInterpreter(List<TfxData> opcodes, BytecodeType type = BytecodeType.Expression)
    {
        Opcodes = opcodes?.ToArray() ?? Array.Empty<TfxData>();
        Type = type;
        _stack = new Vec4[64];
        _temp = new Vec4[16];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<Vec4> StackPop(int count)
    {
        if (_sp < count)
            throw new Exception($"{Name}: Stack underflow. Op {_curOp.op} (sp={_sp}, count={count})");

        _sp -= count;
        return new ReadOnlySpan<Vec4>(_stack, _sp, count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vec4 StackTop()
    {
        if (_sp < 1)
            throw new Exception($"{Name}: Stack underflow. Op {_curOp.op}");
        return _stack[--_sp];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StackPush(Vec4 value)
    {
        if (_sp >= _stack.Length)
            throw new Exception($"{Name}: Stack overflow. Op {_curOp.op} (sp={_sp})");
        _stack[_sp++] = value;
    }

    public async Task<Vec4[]> EvaluateAsync(
        CharmRenderer renderer,
        Vec4[] constants,
        Vec4[] bytecodeConstants,
        SMaterialShader? shader,
        List<SamplerState> samplers,
        ObjectChannels objectChannels,
        bool print = false,
        RendererGlobalChannels globalChannels = null)
    {
        Evaluate(
            renderer,
            constants,
            bytecodeConstants,
            shader,
            samplers,
            objectChannels,
            out var evaluated,
            print,
            globalChannels);

        return evaluated;
    }

    private const string name = "Bytecode Evaluate";
    public void Evaluate(
        CharmRenderer renderer,
        System.Numerics.Vector4[] constants,
        System.Numerics.Vector4[] bytecodeConstants,
        SMaterialShader? shader,
        List<SamplerState> samplers,
        ObjectChannels objectChannels,
        out System.Numerics.Vector4[] evaluated,
        bool print = false,
        RendererGlobalChannels globalChannels = null)
    {
#if DEBUG
        RenderHelpers.Profile(Name);
#endif

        // Reset state
        _sp = 0;
        Array.Clear(_temp, 0, _temp.Length);
        evaluated = (Vec4[])constants?.Clone() ?? default;
        try
        {
            if (print) Log.Info($"--------Evaluating Bytecode:");
            for (int i = 0; i < Opcodes.Length; i++)
            {
                _curOp = Opcodes[i];
                if (print) Log.Info($"{_curOp.op} :");

                switch (_curOp.op)
                {
                    case TfxBytecode.Add:
                    case TfxBytecode.Add2:
                        var add = StackPop(2);
                        StackPush(add[0] + add[1]);
                        break;

                    case TfxBytecode.Subtract:
                        var sub = StackPop(2);
                        StackPush(sub[0] - sub[1]);
                        break;

                    case TfxBytecode.Multiply:
                    case TfxBytecode.Multiply2:
                        var mul = StackPop(2);
                        StackPush(mul[0] * mul[1]);
                        break;

                    case TfxBytecode.Divide:
                        var div = StackPop(2);
                        StackPush(div[0] / div[1]);
                        break;

                    case TfxBytecode.IsZero:
                        var isZero = StackTop();
                        StackPush(new Vec4(isZero.X == 0 ? 1 : 0,
                            isZero.Y == 0 ? 1 : 0,
                            isZero.Z == 0 ? 1 : 0,
                            isZero.W == 0 ? 1 : 0));
                        break;

                    case TfxBytecode.Min:
                        var min = StackPop(2);
                        StackPush(Vec4.Min(min[0], min[1]));
                        break;

                    case TfxBytecode.Max:
                        var max = StackPop(2);
                        StackPush(Vec4.Max(max[0], max[1]));
                        break;

                    case TfxBytecode.LessThan: //I dont think I need to do < for each element?
                        var lessThan = StackPop(2);
                        StackPush(new Vec4(lessThan[0].X < lessThan[1].X ? 1 : 0,
                            lessThan[0].Y < lessThan[1].Y ? 1 : 0,
                            lessThan[0].Z < lessThan[1].Z ? 1 : 0,
                            lessThan[0].W < lessThan[1].W ? 1 : 0));

                        break;

                    case TfxBytecode.Dot:
                        var dot = StackPop(2);
                        StackPush(new Vec4(Vec4.Dot(dot[0], dot[1])));
                        break;

                    case TfxBytecode.Merge_1_3:
                        var merge = StackPop(2);
                        StackPush(new Vec4(merge[0].X, merge[1].X, merge[1].Y, merge[1].Z));
                        break;

                    case TfxBytecode.Merge_2_2:
                        var merge2_2 = StackPop(2);
                        StackPush(new Vec4(merge2_2[0].X, merge2_2[0].Y, merge2_2[1].X, merge2_2[1].Y));
                        break;

                    case TfxBytecode.Merge_3_1:
                        var merge3_1 = StackPop(2);
                        StackPush(new Vec4(merge3_1[0].X, merge3_1[0].Y, merge3_1[0].Z, merge3_1[1].X));
                        break;

                    case TfxBytecode.Cubic:
                        var cubic = StackPop(2);
                        StackPush(TFXFunctions.bytecode_op_cubic(cubic[0], cubic[1]));
                        break;

                    case TfxBytecode.Lerp:
                    case TfxBytecode.LerpSaturated:
                        {
                            var lerp = StackPop(3);
                            var result = lerp[0] + lerp[2] * (lerp[1] - lerp[0]);
                            if (_curOp.op == TfxBytecode.LerpSaturated)
                                result = result.Clamp(Vec4.Zero, Vec4.One);

                            StackPush(result);
                            break;
                        }

                    case TfxBytecode.MultiplyAdd:
                        var mulAdd = StackPop(3);
                        StackPush(mulAdd[0] * mulAdd[1] + mulAdd[2]);
                        break;

                    case TfxBytecode.Clamp:
                        var clamp = StackPop(3);
                        StackPush(Vec4.Clamp(clamp[1], clamp[2], clamp[0]));
                        break;

                    case TfxBytecode.Unk14:
                        var smoothStep = StackPop(3);
                        StackPush(TFXFunctions.EvaluateSmoothStep(smoothStep[2], smoothStep[1], smoothStep[0]));
                        break;

                    case TfxBytecode.Abs:
                        StackPush(Vec4.Abs(StackTop()));
                        break;

                    case TfxBytecode.Sign:
                        var sign = StackTop();
                        StackPush(new Vec4(
                            Math.Sign(sign.X),
                            Math.Sign(sign.Y),
                            Math.Sign(sign.Z),
                            Math.Sign(sign.W)
                        ));
                        break;

                    case TfxBytecode.Floor:
                        var floor = StackTop();
                        StackPush(new Vec4(
                                MathF.Floor(floor.X),
                                MathF.Floor(floor.Y),
                                MathF.Floor(floor.Z),
                                MathF.Floor(floor.W)
                            ));
                        break;

                    case TfxBytecode.Ceil:
                        var ceil = StackTop();
                        StackPush(new Vec4(
                                MathF.Ceiling(ceil.X),
                                MathF.Ceiling(ceil.Y),
                                MathF.Ceiling(ceil.Z),
                                MathF.Ceiling(ceil.W)
                            ));
                        break;

                    case TfxBytecode.Round:
                        var round = StackTop();
                        StackPush(new Vec4(
                                MathF.Round(round.X),
                                MathF.Round(round.Y),
                                MathF.Round(round.Z),
                                MathF.Round(round.W)
                            ));
                        break;

                    case TfxBytecode.Frac:
                        var frac = StackTop();
                        StackPush(new Vec4(
                                frac.X - MathF.Floor(frac.X),
                                frac.Y - MathF.Floor(frac.Y),
                                frac.Z - MathF.Floor(frac.Z),
                                frac.W - MathF.Floor(frac.W)
                            ));
                        break;

                    case TfxBytecode.Unk1b:
                        StackPush(TFXFunctions.EvaluateNormalize4(StackTop()));
                        break;

                    case TfxBytecode.Unk1c:
                        StackPush(TFXFunctions.EvaluateNormalize3(StackTop()));
                        //StackPush(TFXFunctions.BytecodeOpUnk1C(StackTop()));
                        break;

                    case TfxBytecode.Negate:
                        StackPush(Vec4.Negate(StackTop()));
                        break;

                    case TfxBytecode.VecRotSin:
                        StackPush(TFXFunctions._trig_helper_vector_sin_rotations_estimate(StackTop()));
                        break;

                    case TfxBytecode.VecRotCos:
                        StackPush(TFXFunctions._trig_helper_vector_cos_rotations_estimate(StackTop()));
                        break;

                    case TfxBytecode.VecRotSinCos:
                        StackPush(TFXFunctions._trig_helper_vector_sin_cos_rotations_estimate(StackTop()));
                        break;

                    case TfxBytecode.PermuteAllX:
                        StackPush(new Vec4(StackTop().X));
                        break;

                    case TfxBytecode.Permute:
                        var fields = ((TfxData1Byte)_curOp.data).value;
                        var permute = StackTop();

                        StackPush(Permute(permute, fields));
                        break;

                    case TfxBytecode.Saturate:
                        var saturate = StackTop();
                        StackPush(TFXFunctions.Saturate(saturate));
                        break;

                    case TfxBytecode.Unk25:
                        var unk25 = StackTop();
                        StackPush(new Vec4(
                                MathF.Log2(unk25.X),
                                MathF.Log2(unk25.Y),
                                MathF.Log2(unk25.Z),
                                MathF.Log2(unk25.W)
                            ));
                        break;

                    case TfxBytecode.Unk26: // idk
                        StackPush(TFXFunctions.EvaluateLength4(StackTop()));
                        break;

                    case TfxBytecode.Triangle:
                        StackPush(TFXFunctions.bytecode_op_triangle(StackTop()));
                        break;

                    case TfxBytecode.Jitter:
                        StackPush(TFXFunctions.bytecode_op_jitter(StackTop()));
                        break;

                    case TfxBytecode.Wander:
                        StackPush(TFXFunctions.bytecode_op_wander(StackTop()));
                        break;

                    case TfxBytecode.Rand:
                        StackPush(TFXFunctions.bytecode_op_rand(StackTop()));
                        break;

                    case TfxBytecode.RandSmooth:
                        StackPush(TFXFunctions.bytecode_op_rand_smooth(StackTop()));
                        break;

                    case TfxBytecode.TransformVec4:
                        StackPush(TFXFunctions.mul_vec4(StackPop(5)));
                        break;

                    case TfxBytecode.PushConstantVec4:
                        var vec = bytecodeConstants[((TfxData1Byte)_curOp.data).value];
                        StackPush(vec);
                        break;

                    // Compare ops? these are guesses
                    case TfxBytecode.Unk34_EoF:
                        var cmpLT = StackPop(2);
                        bool allLess = cmpLT[0].X < cmpLT[1].X
                            && cmpLT[0].Y < cmpLT[1].Y
                            && cmpLT[0].Z < cmpLT[1].Z
                            && cmpLT[0].W < cmpLT[1].W;
                        StackPush(allLess ? Vec4.One : Vec4.Zero);
                        break;

                    case TfxBytecode.Unk35_EoF:
                        var cmpLTE = StackPop(2);
                        bool allLessEqual = cmpLTE[0].X <= cmpLTE[1].X
                            && cmpLTE[0].Y <= cmpLTE[1].Y
                            && cmpLTE[0].Z <= cmpLTE[1].Z
                            && cmpLTE[0].W <= cmpLTE[1].W;
                        StackPush(allLessEqual ? Vec4.One : Vec4.Zero);
                        break;

                    case TfxBytecode.Unk36_EoF:
                        var cmpGT = StackPop(2);
                        bool allGreater = cmpGT[0].X > cmpGT[1].X
                            && cmpGT[0].Y > cmpGT[1].Y
                            && cmpGT[0].Z > cmpGT[1].Z
                            && cmpGT[0].W > cmpGT[1].W;
                        StackPush(allGreater ? Vec4.One : Vec4.Zero);
                        break;

                    case TfxBytecode.Unk37_EoF:
                        var cmpGTE = StackPop(2);
                        bool allGreaterEqual = cmpGTE[0].X >= cmpGTE[1].X
                            && cmpGTE[0].Y >= cmpGTE[1].Y
                            && cmpGTE[0].Z >= cmpGTE[1].Z
                            && cmpGTE[0].W >= cmpGTE[1].W;
                        StackPush(allGreaterEqual ? Vec4.One : Vec4.Zero);
                        break;

                    case TfxBytecode.Unk38_EoF:
                        var cmpEqual = StackPop(2);
                        StackPush(cmpEqual[0] == cmpEqual[1] ? Vec4.One : Vec4.Zero);
                        break;

                    case TfxBytecode.Unk39_EoF:
                        var cmpNotEqual = StackPop(2);
                        StackPush(cmpNotEqual[0] != cmpNotEqual[1] ? Vec4.One : Vec4.Zero);
                        break;

                    case TfxBytecode.Unk3A_EoF:
                        StackPush(StackTop() != Vec4.Zero ? Vec4.One : Vec4.Zero);
                        break;

                    /// End of compare ops

                    case TfxBytecode.LerpConstant:
                    case TfxBytecode.LerpConstantSaturated:
                        {
                            var t = StackTop();
                            var a = bytecodeConstants[((TfxData1Byte)_curOp.data).value];
                            var b = bytecodeConstants[((TfxData1Byte)_curOp.data).value + 1];

                            var result = a + t * (b - a);
                            if (_curOp.op == TfxBytecode.LerpConstantSaturated)
                                result = result.Clamp(Vec4.Zero, Vec4.One);

                            StackPush(result);
                            break;
                        }

                    case TfxBytecode.Spline4Const:
                        var s4c_index = ((TfxData1Byte)_curOp.data).value;
                        var X = StackTop();
                        var C3 = bytecodeConstants[s4c_index];
                        var C2 = bytecodeConstants[s4c_index + 1];
                        var C1 = bytecodeConstants[s4c_index + 2];
                        var C0 = bytecodeConstants[s4c_index + 3];
                        var threshold = bytecodeConstants[s4c_index + 4];

                        StackPush(TFXFunctions.bytecode_op_spline4_const(X, C3, C2, C1, C0, threshold));
                        break;

                    case TfxBytecode.Spline8Const:
                        var s8c_index = ((TfxData1Byte)_curOp.data).value;
                        var X_1 = StackTop();
                        var C_thresholds = bytecodeConstants[s8c_index + 8];
                        var D_thresholds = bytecodeConstants[s8c_index + 9];
                        var C3_1 = bytecodeConstants[s8c_index];
                        var C2_1 = bytecodeConstants[s8c_index + 1];
                        var C1_1 = bytecodeConstants[s8c_index + 2];
                        var C0_1 = bytecodeConstants[s8c_index + 3];
                        var D3 = bytecodeConstants[s8c_index + 4];
                        var D2 = bytecodeConstants[s8c_index + 5];
                        var D1 = bytecodeConstants[s8c_index + 6];
                        var D0 = bytecodeConstants[s8c_index + 7];

                        StackPush(TFXFunctions.bytecode_op_spline8_const(X_1, C3_1, C2_1, C1_1, C0_1, D3, D2, D1, D0, C_thresholds, D_thresholds));
                        break;

                    case TfxBytecode.Spline8ConstChain:
                        var s8cc_index = ((TfxData1Byte)_curOp.data).value;
                        if (s8cc_index + 9 >= bytecodeConstants.Length)
                            throw new IndexOutOfRangeException($"Max index out of range: Start index: {s8cc_index}, End index {s8cc_index + 9}. Constants count: {bytecodeConstants.Length}");

                        ReadOnlySpan<Vec4> stack = StackPop(2);
                        //Console.WriteLine($"X {stack[1]}, Recursion {stack[0]}");
                        C3_1 = bytecodeConstants[s8cc_index + 0];
                        C2_1 = bytecodeConstants[s8cc_index + 1];
                        C1_1 = bytecodeConstants[s8cc_index + 2];
                        C0_1 = bytecodeConstants[s8cc_index + 3];
                        D3 = bytecodeConstants[s8cc_index + 4];
                        D2 = bytecodeConstants[s8cc_index + 5];
                        D1 = bytecodeConstants[s8cc_index + 6];
                        D0 = bytecodeConstants[s8cc_index + 7];
                        C_thresholds = bytecodeConstants[s8cc_index + 8];
                        D_thresholds = bytecodeConstants[s8cc_index + 9];
                        StackPush(TFXFunctions.bytecode_op_spline8_chain_const(stack[1], stack[0], C3_1, C2_1, C1_1, C0_1, D3, D2, D1, D0, C_thresholds, D_thresholds));
                        break;

                    case TfxBytecode.Gradient4Const:
                        var g4c_index = ((TfxData1Byte)_curOp.data).value;
                        var X_g4c = StackTop();
                        var BaseColor = bytecodeConstants[g4c_index];
                        var Cred = bytecodeConstants[g4c_index + 1];
                        var Cgreen = bytecodeConstants[g4c_index + 2];
                        var Cblue = bytecodeConstants[g4c_index + 3];
                        var Calpha = bytecodeConstants[g4c_index + 4];
                        var Cthresholds = bytecodeConstants[g4c_index + 5];

                        StackPush(TFXFunctions.bytecode_op_gradient4_const(X_g4c, BaseColor, Cred, Cgreen, Cblue, Calpha, Cthresholds));
                        break;

                    case TfxBytecode.Gradient8Const:
                        var g8c_index = ((TfxData1Byte)_curOp.data).value;
                        var g8c_X1 = StackTop();
                        var g8c_BaseColor = bytecodeConstants[g8c_index];
                        var g8c_Cred = bytecodeConstants[g8c_index + 1];
                        var g8c_Cgreen = bytecodeConstants[g8c_index + 2];
                        var g8c_Cblue = bytecodeConstants[g8c_index + 3];
                        var g8c_Calpha = bytecodeConstants[g8c_index + 4];
                        var g8c_Dred = bytecodeConstants[g8c_index + 5];
                        var g8c_Dgreen = bytecodeConstants[g8c_index + 6];
                        var g8c_Dblue = bytecodeConstants[g8c_index + 7];
                        var g8c_Dalpha = bytecodeConstants[g8c_index + 8];
                        var g8c_Cthresholds = bytecodeConstants[g8c_index + 9];
                        var g8c_Dthresholds = bytecodeConstants[g8c_index + 10];

                        StackPush(TFXFunctions.bytecode_op_gradient8_const(g8c_X1, g8c_BaseColor, g8c_Cred, g8c_Cgreen, g8c_Cblue, g8c_Calpha, g8c_Dred, g8c_Dgreen, g8c_Dblue, g8c_Dalpha, g8c_Cthresholds, g8c_Dthresholds));
                        break;

                    case TfxBytecode.PushExternInputFloat:
                        if (Type == BytecodeType.Sequencer)
                        {
                            if (globalChannels is not null)
                                StackPush(globalChannels.MiscValues[((TfxData2Byte)_curOp.data).value2]);
                            break;
                        }

                        var PushExternInputFloat = renderer.Externs.Get<float>((TfxExtern)((TfxData2Byte)_curOp.data).value, ((TfxData2Byte)_curOp.data).value2 * 4);
                        StackPush(new(PushExternInputFloat));
                        break;

                    case TfxBytecode.PushExternInputVec4:
                        var PushExternInputVec4 = renderer.Externs.Get<Vec4>((TfxExtern)((TfxData2Byte)_curOp.data).value, ((TfxData2Byte)_curOp.data).value2 * 16);
                        StackPush(PushExternInputVec4);
                        break;

                    case TfxBytecode.PushExternInputMat4:
                        var PushExternInputMat4 = renderer.Externs.Get<Matrix4x4ButGood>((TfxExtern)((TfxData2Byte)_curOp.data).value, ((TfxData2Byte)_curOp.data).value2 * 16);
                        StackPush(PushExternInputMat4.X);
                        StackPush(PushExternInputMat4.Y);
                        StackPush(PushExternInputMat4.Z);
                        StackPush(PushExternInputMat4.W);
                        break;

                    case TfxBytecode.Unk42:
                    case TfxBytecode.Unk4c:
                        StackPush(Vec4.One);
                        break;
                    case TfxBytecode.Unk50:
                        StackPush(Vec4.Zero);
                        break;
                    case TfxBytecode.Unk2c:
                    case TfxBytecode.Unk49:
                    case TfxBytecode.Unk51:
                        _ = StackPop(1);
                        break;
                    case TfxBytecode.Unk2d:
                        _ = StackPop(4);
                        break;

                    case TfxBytecode.PushGlobalChannelVector:
                        var index = ((TfxData1Byte)_curOp.data).value;
                        var global_channel = globalChannels?.Get(index) ?? GlobalChannels.GetDefault(index);
                        StackPush(global_channel);
                        break;

                    case TfxBytecode.PushTexDimensions:
                        var ptd = ((TfxData2Byte)_curOp.data);
                        if (_pushTexCache[ptd.value] == null)
                        {
                            Texture texture = FileResourcer.Get().GetFile<Texture>(shader.Value.EnumerateSamplers().ToList()[ptd.value].Hash);
                            _pushTexCache[ptd.value] = texture;
                        }

                        var tex = _pushTexCache[ptd.value];
                        StackPush(Permute(new(tex.TagData.Width, tex.TagData.Height, tex.TagData.Depth, tex.TagData.ArraySize), ptd.value2));
                        break;

                    case TfxBytecode.PushTexTileParams:
                        var ptt = ((TfxData2Byte)_curOp.data);
                        if (_pushTexCache[ptt.value] == null)
                        {
                            Texture texture = FileResourcer.Get().GetFile<Texture>(shader.Value.EnumerateSamplers().ToList()[ptt.value].Hash);
                            _pushTexCache[ptt.value] = texture;
                        }

                        tex = _pushTexCache[ptt.value];
                        StackPush(Permute(tex.TagData.TilingScaleOffset, ptt.value2));
                        break;

                    case TfxBytecode.PushTexTileCount:
                        var pttc = ((TfxData2Byte)_curOp.data);
                        if (_pushTexCache[pttc.value] == null)
                        {
                            Texture texture = FileResourcer.Get().GetFile<Texture>(shader.Value.EnumerateSamplers().ToList()[pttc.value].Hash);
                            _pushTexCache[pttc.value] = texture;
                        }

                        tex = _pushTexCache[pttc.value];
                        StackPush(Permute(new(tex.TagData.TileCount, tex.TagData.ArraySize, 0, 0), pttc.value2));
                        break;

                    case TfxBytecode.PushExternInputTextureView:
                        var data = (TfxData2Byte)_curOp.data;
                        var extern_id = data.value;
                        int offset = data.value2 * 8;
                        var bits = ((int)extern_id) << 24 | (offset & 0xFFFFFF);

                        StackPush(new Vec4(BitConverter.Int32BitsToSingle(bits), 0, 0, 0));
                        break;

                    case TfxBytecode.PushExternInputUav: // TODO
                    case TfxBytecode.SetShaderUav:
                        break;

                    case TfxBytecode.SetShaderTexture:
                        index = ((TfxData1Byte)_curOp.data).value;
                        var shader_stage = ShaderStageExtensions.FromIndex((byte)(index >> 5));
                        var slot = index & 0x1F;
                        bits = BitConverter.SingleToInt32Bits(StackTop().X);

                        extern_id = (byte)(bits >> 24);
                        offset = bits & 0xFFFFFF;
                        var srv = renderer.Externs.Get<ShaderResourceView>((TfxExtern)extern_id, offset);
                        //Console.WriteLine($"{Name} {slot} ({extern_id} ({(byte)(bits >> 24)}), {offset:X}) : {srv?.DebugName}");
                        switch (shader_stage)
                        {
                            case ShaderStage.Vertex:
                                renderer.Context.VertexShader.SetShaderResource(slot, srv);
                                break;

                            case ShaderStage.Pixel:
                                renderer.Context.PixelShader.SetShaderResource(slot, srv);
                                break;

                            default:
                                break;
                        }
                        break;

                    case TfxBytecode.SetShaderSampler:
                        var setShaderSamplerData = ((TfxData1Byte)_curOp.data).value;
                        shader_stage = ShaderStageExtensions.FromIndex((byte)(setShaderSamplerData >> 5));
                        slot = setShaderSamplerData & 0x1F;
                        int samplerIndex = BitConverter.SingleToInt32Bits(StackTop().X);
                        //Console.WriteLine($"{Name} {slot} : {samplers[samplerIndex].Description.AddressU}");

                        switch (shader_stage)
                        {
                            case ShaderStage.Vertex:
                                renderer.Context.VertexShader.SetSampler(slot, samplers[samplerIndex]);
                                break;

                            case ShaderStage.Pixel:
                                renderer.Context.PixelShader.SetSampler(slot, samplers[samplerIndex]);
                                break;

                            default:
                                break;
                        }

                        break;

                    case TfxBytecode.PushSampler:
                        var pushSamplerData = (TfxData1Byte)_curOp.data;
                        index = pushSamplerData.value;
                        if (index < 0 || index >= samplers.Count)
                            throw new Exception($"{Name}: Sampler index out of range");

                        StackPush(new Vec4(BitConverter.Int32BitsToSingle(index), 0, 0, 0));
                        break;

                    case TfxBytecode.PushObjectChannelVector:
                        var hash = ((TfxDataUint)_curOp.data).value;
                        var channel = objectChannels?.Channels[hash].Vec4 ?? Vector4.One;
                        StackPush(channel);
                        break;

                    case TfxBytecode.PushFromOutput:
                        StackPush(evaluated[((TfxData1Byte)_curOp.data).value]);
                        break;

                    case TfxBytecode.PopOutput:
                        if (print) Log.Info($"----Output Stack Count: {_sp}");
                        if (_sp != 0)
                        {
                            var top = StackTop();
                            var outSlot = ((TfxData1Byte)_curOp.data).value;
                            evaluated[outSlot] = top;
                            if (print) Log.Info($"----Output: {top} to slot {outSlot}");
                        }
                        break;

                    case TfxBytecode.PopOutputMat4:
                        var mat = StackPop(4);
                        slot = ((TfxData1Byte)_curOp.data).value;
                        evaluated[slot] = mat[0];
                        evaluated[slot + 1] = mat[1];
                        evaluated[slot + 2] = mat[2];
                        evaluated[slot + 3] = mat[3];
                        _sp = 0;
                        break;

                    case TfxBytecode.PushTemp:
                        var pushTemp = ((TfxData1Byte)_curOp.data).value;
                        if (pushTemp >= _temp.Length)
                            throw new Exception($"{Name}: Temp index out of range");
                        StackPush(_temp[pushTemp]);
                        break;

                    case TfxBytecode.PopTemp:
                        var popTemp = ((TfxData1Byte)_curOp.data).value;
                        if (popTemp >= _temp.Length)
                            throw new Exception($"{Name}: Temp index out of range");
                        _temp[popTemp] = StackTop();
                        break;

                    default:
                        Log.Error($"{Name}: Not Implemented: {_curOp.op} (0x{_curOp.rawOp:X2})");
                        break;

                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"{Name}: Current Op {_curOp.op} (0x{_curOp.rawOp:X2}): {e.Message}");
            throw new Exception($"{Name}: Error evaluating bytecode at opcode {_curOp.op} (0x{_curOp.rawOp:X2}) with data {_curOp.data}.\nOpCodes: {string.Join("\n", Opcodes.Select(x => x.op))}", e);
        }

#if DEBUG
        RenderHelpers.EndProfile();
#endif
    }

    private Vec4 Permute(Vec4 permute, byte fields)
    {
        float x = ((fields >> 6) & 0b11) switch
        {
            0 => permute.X,
            1 => permute.Y,
            2 => permute.Z,
            3 => permute.W,
            _ => 0
        };
        float y = ((fields >> 4) & 0b11) switch
        {
            0 => permute.X,
            1 => permute.Y,
            2 => permute.Z,
            3 => permute.W,
            _ => 0
        };
        float z = ((fields >> 2) & 0b11) switch
        {
            0 => permute.X,
            1 => permute.Y,
            2 => permute.Z,
            3 => permute.W,
            _ => 0
        };
        float w = (fields & 0b11) switch
        {
            0 => permute.X,
            1 => permute.Y,
            2 => permute.Z,
            3 => permute.W,
            _ => 0
        };

        return new(x, y, z, w);
    }
}
