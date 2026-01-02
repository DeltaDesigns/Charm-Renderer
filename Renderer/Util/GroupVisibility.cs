using System.Runtime.CompilerServices;

namespace Charm.Renderer;

public sealed class GroupVisibility
{
	private readonly bool[] _visible;
	private readonly HashSet<RenderObject> _affectedObjects = new();

	public GroupVisibility(int maxGroupIndex)
	{
		_visible = new bool[maxGroupIndex + 1];
		Array.Fill(_visible, true);
	}

	public bool HasObjectFilter => _affectedObjects.Count > 0;

	public void AddObject(RenderObject obj) => _affectedObjects.Add(obj);
	public void RemoveObject(RenderObject obj) => _affectedObjects.Remove(obj);
	public void Clear()
	{
		_affectedObjects.Clear();
		Array.Fill(_visible, true);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsVisible(RenderObject owner, int groupIndex)
	{
		if (_affectedObjects.Count > 0 && !_affectedObjects.Contains(owner))
			return true;

		return (uint)groupIndex >= _visible.Length || _visible[groupIndex];
	}

	public void SetVisible(int groupIndex, bool visible)
	{
		if ((uint)groupIndex < _visible.Length)
			_visible[groupIndex] = visible;
	}
}
