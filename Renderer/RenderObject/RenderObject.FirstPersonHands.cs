using SharpDX;
using SharpDX.Direct3D11;
using Tiger.Schema.Animation;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace Charm.Renderer;

public partial class RenderObject
{
    public RenderObject? FirstPersonRigSource;
    public Func<Tiger.Schema.MeshPart, bool>? PartFilter;
    private Mat4[]? _handsInverseBind;
    private bool _handsChecked, _handsMatch;

    private bool IsShown(CharmRenderer renderer) => Visible && (FirstPersonRigSource == null || FirstPersonRigSource.PoseSource.DrawsFirstPersonRig(renderer));

    private void UpdateFirstPersonHands(CharmRenderer renderer)
    {
        var source = FirstPersonRigSource!.PoseSource;
        source.UpdateAnimation(renderer);
        if (source._skinning is not SkinningFrames frames || !HandsMatchRig(renderer, source))
            return;

        for (int i = 0; i < Bones!.Count; i++)
            SkinningFrames.WriteSkin(frames.RigBoneTransform(i), _handsInverseBind![i], _boneRows!, i * SkinningFrames.FloatsPerBone);

        _poseVersion++;

        DataBox dataBox = renderer.Context.MapSubresource(_boneBuffer, 0, MapMode.WriteDiscard, MapFlags.None);
        try
        {
            Utilities.Write(dataBox.DataPointer, _boneRows!, 0, _boneRows!.Length);
        }
        finally
        {
            renderer.Context.UnmapSubresource(_boneBuffer, 0);
        }
    }

    private bool HandsMatchRig(CharmRenderer renderer, RenderObject source)
    {
        if (_handsChecked)
            return _handsMatch;

        _handsChecked = true;

        _handsInverseBind = Bones.Select(x => Mat4.FromTransform(x.DefaultInverseObjectSpaceTransform)).ToArray();
        _boneRows = new float[Bones.Count * SkinningFrames.FloatsPerBone];
        _boneBuffer = new Buffer(renderer.Device, new BufferDescription
        {
            SizeInBytes = Bones.Count * 3 * 16,
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.Write,
            OptionFlags = ResourceOptionFlags.BufferStructured,
            StructureByteStride = 16,
        });

        _boneSRV = new ShaderResourceView(renderer.Device, _boneBuffer);
        foreach (var mesh in _meshes)
            mesh.BoneRowsSRV = _boneSRV;

        return _handsMatch = true;
    }
}
