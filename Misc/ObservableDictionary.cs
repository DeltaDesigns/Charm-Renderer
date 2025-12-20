using System.Collections.Specialized;
using System.ComponentModel;

public class ObservableDictionary<TKey, TValue> : IDictionary<TKey, TValue>,
    INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly Dictionary<TKey, TValue> _dict = new Dictionary<TKey, TValue>();

    public event NotifyCollectionChangedEventHandler CollectionChanged;
    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        => CollectionChanged?.Invoke(this, e);

    public void Add(TKey key, TValue value)
    {
        _dict.Add(key, value);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, new KeyValuePair<TKey, TValue>(key, value)));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(Keys));
        OnPropertyChanged(nameof(Values));
    }

    public bool Remove(TKey key)
    {
        if (_dict.TryGetValue(key, out var value) && _dict.Remove(key))
        {
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Remove, new KeyValuePair<TKey, TValue>(key, value)));
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(Keys));
            OnPropertyChanged(nameof(Values));
            return true;
        }
        return false;
    }

    public TValue this[TKey key]
    {
        get => _dict[key];
        set
        {
            bool exists = _dict.ContainsKey(key);
            var oldValue = exists ? _dict[key] : default;
            _dict[key] = value;

            if (exists)
            {
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Replace,
                    new KeyValuePair<TKey, TValue>(key, value),
                    new KeyValuePair<TKey, TValue>(key, oldValue)));
            }
            else
            {
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Add,
                    new KeyValuePair<TKey, TValue>(key, value)));
                OnPropertyChanged(nameof(Count));
                OnPropertyChanged(nameof(Keys));
                OnPropertyChanged(nameof(Values));
            }
        }
    }

    public void Clear()
    {
        _dict.Clear();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(Keys));
        OnPropertyChanged(nameof(Values));
    }

    // IDictionary members
    public ICollection<TKey> Keys => _dict.Keys;
    public ICollection<TValue> Values => _dict.Values;
    public int Count => _dict.Count;
    public bool ContainsKey(TKey key) => _dict.ContainsKey(key);
    public bool TryGetValue(TKey key, out TValue value) => _dict.TryGetValue(key, out value);
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _dict.GetEnumerator();

    void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);
    bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) => Remove(item.Key);
    bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        => _dict.ContainsKey(item.Key) && EqualityComparer<TValue>.Default.Equals(_dict[item.Key], item.Value);
    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        => ((ICollection<KeyValuePair<TKey, TValue>>)_dict).CopyTo(array, arrayIndex);
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;
}
