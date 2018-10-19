namespace AvalonStudio.Debugging
{
    using Avalonia.Media;
    using Avalonia.Threading;
    using AvalonStudio.Extensibility;
    using AvalonStudio.Extensibility.Studio;
    using AvalonStudio.MVVM;
    using AvalonStudio.Shell;
    using Mono.Debugging.Client;
    using ReactiveUI;
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Composition;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class ObjectValueViewModel2
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public ObjectValue Object { get; set; }
        public bool CanEditName { get; set; }
        public bool CanEditValue { get; set; }
        public string Icon { get; set; }
        public IBrush NameColor { get; set; }
        public IBrush ValueColor { get; set; }
        public bool EvaluateStatusIconVisible { get; set; }
        public string EvaluateStatusIcon { get; set; }
        public bool ValueButtonVisible { get; set; }
        public string ValueButtonText { get; set; }
        public bool ViewerButtonVisible { get; set; }
        public string PreviewIcon { get; set; }
        public string PinIcon { get; set; }
        public string LiveUpdateIcon { get; set; }

        public ObjectValueViewModel2 Parent { get; }
        public ObservableCollection<ObjectValueViewModel2> Children { get; } = new ObservableCollection<ObjectValueViewModel2>();
    }

    public class WatchListTreeViewModel
    {
        private bool _disposed;
        private bool showExpanders;
        private StackFrame _frame;
        private readonly Dictionary<string, ObjectValue> _cachedValues = new Dictionary<string, ObjectValue>();
        private readonly Dictionary<ObjectValue, ObjectValueViewModel2> _nodes = new Dictionary<ObjectValue, ObjectValueViewModel2>();
        private readonly List<ObjectValue> _values = new List<ObjectValue>();
        private readonly Dictionary<string, string> _oldValues = new Dictionary<string, string>();

        public ObservableCollection<ObjectValueViewModel2> Children { get; } = new ObservableCollection<ObjectValueViewModel2>();

        private void Update ()
        {
            _cachedValues.Clear();
            Refresh(true);
        }

        private void Refresh(bool reset)
        {
            foreach(var val in _nodes.Keys.ToList())
            {
                UnregisterValue(val);
            }

            _nodes.Clear();

            foreach(var val in _values)
            {
                AppendValue(null, null, val);

                if (val.HasChildren)
                {
                    showExpanders = true;
                }
            }
        }

        void AppendValue(ObjectValueViewModel2 parent, string name, ObjectValue val)
        {
            var iter = new ObjectValueViewModel2();

            if (parent == null)
            {
                Children.Add(iter);
            }
            else
            {
                parent.Children.Add(iter);
            }

            SetValues(parent, iter, name, val);
            RegisterValue(val, iter);
        }

        void RegisterValue(ObjectValue val, ObjectValueViewModel2 iter)
        {
            if (val.IsEvaluating)
            {
                _nodes[val] = iter;
                val.ValueChanged += OnValueUpdated;
            }
        }

        public static string GetIcon(ObjectValueFlags flags)
        {
            if ((flags & ObjectValueFlags.Field) != 0 && (flags & ObjectValueFlags.ReadOnly) != 0)
                return "md-literal";

            string global = (flags & ObjectValueFlags.Global) != 0 ? "static-" : string.Empty;
            string source;

            switch (flags & ObjectValueFlags.OriginMask)
            {
                case ObjectValueFlags.Property: source = "property"; break;
                case ObjectValueFlags.Type: source = "class"; global = string.Empty; break;
                case ObjectValueFlags.Method: source = "method"; break;
                case ObjectValueFlags.Literal: return "md-literal";
                case ObjectValueFlags.Namespace: return "md-name-space";
                case ObjectValueFlags.Group: return "md-open-resource-folder";
                case ObjectValueFlags.Field: source = "field"; break;
                case ObjectValueFlags.Variable: return "md-variable";
                default: return "md-empty";
            }

            string access;
            switch (flags & ObjectValueFlags.AccessMask)
            {
                case ObjectValueFlags.Private: access = "private-"; break;
                case ObjectValueFlags.Internal: access = "internal-"; break;
                case ObjectValueFlags.InternalProtected:
                case ObjectValueFlags.Protected: access = "protected-"; break;
                default: access = string.Empty; break;
            }

            return "md-" + access + global + source;
        }

        public void SetValues(ObjectValueViewModel2 parent, ObjectValueViewModel2 it, string name, ObjectValue val, bool updateJustValue = false)
        {
            string strval;
            bool canEdit;
            string nameColor = null;
            string valueColor = null;
            string valueButton = null;
            string evaluateStatusIcon = null;

            name = name ?? val.Name;

            bool showViewerButton = false;

            string valPath;

            if (parent == null)
            {
                valPath = "/" + name;
            }
            else
            {
                valPath = GetIterPath(parent) + "/" + name;
            }

            _oldValues.TryGetValue(valPath, out string oldValue);

            if (val.IsUnknown)
            {
                if (_frame != null)
                {
                    strval = $"The name '{val.Name}' does not exist in the current context.";
                    // TODO set value error?
                    nameColor = "object value tree disabled color";//Ide.Gui.Styles.ColorGetHex(Styles.ObjectValueTreeValueDisabledText);
                    canEdit = false;
                }
                else
                {
                    canEdit = !val.IsReadOnly;
                    strval = string.Empty;
                }

                evaluateStatusIcon = "Warning";
            }
            else if (val.IsError || val.IsNotSupported)
            {
                evaluateStatusIcon = "Warning";
                strval = val.Value;
                int i = strval.IndexOf('\n');
                if (i != -1)
                    strval = strval.Substring(0, i);
                valueColor = "object value treeerror color";//Ide.Gui.Styles.ColorGetHex(Styles.ObjectValueTreeValueErrorText);
                canEdit = false;
            }
            else if (val.IsImplicitNotSupported)
            {
                strval = "";//val.Value; with new "Show Value" button we don't want to display message "Implicit evaluation is disabled"
                valueColor = "object value tree disabled color"; //Ide.Gui.Styles.ColorGetHex(Styles.ObjectValueTreeValueDisabledText);

                if (val.CanRefresh)
                {
                    valueButton = "Show Value";
                }

                canEdit = false;
            }
            else if (val.IsEvaluating)
            {
                strval = "Evaluating...";

                evaluateStatusIcon = "md-spinner-16";

                valueColor = "object value tree disabled color";
                if (val.IsEvaluatingGroup)
                {
                    nameColor = "object value tree disabled color";
                    name = val.Name;
                }
                canEdit = false;
            }
            else if (val.Flags.HasFlag(ObjectValueFlags.IEnumerable))
            {
                if (val.Name == "")
                {
                    valueButton = "Show More";
                }
                else
                {
                    valueButton = "Show Values";
                }
                strval = "";
                canEdit = false;
            }
            else
            {
                //???
                showViewerButton = !val.IsNull && true; // capability of debug manager DebuggingService.HasValueVisualizers(val);
                canEdit = val.IsPrimitive && !val.IsReadOnly;
                if (!val.IsNull && false) // debugger service can visualize this type DebuggingService.HasInlineVisualizer(val))
                {
                    try
                    {
                        strval = ""; // the type of visualizer //DebuggingService.GetInlineVisualizer(val).InlineVisualize(val);
                    }
                    catch (Exception)
                    {
                        strval = val.DisplayValue ?? "(null)";
                    }
                }
                else
                {
                    strval = val.DisplayValue ?? "(null)";
                }
                if (oldValue != null && strval != oldValue)
                {
                    nameColor = valueColor = "modified row color"; //Ide.Gui.Styles.ColorGetHex(Styles.ObjectValueTreeValueModifiedText);
                }
            }

            strval = strval.Replace("\r\n", " ").Replace("\n", " ");

            it.Value = strval;
            if (updateJustValue)
                return;

            bool hasChildren = val.HasChildren;
            string icon = GetIcon(val.Flags);
            it.Name = name;
            it.Type = val.TypeName;
            it.Object = val;
            it.CanEditName = parent == null && AllowAdding;
            it.CanEditValue = canEdit && AllowEditing;
            it.Icon = icon;
            //it.NameColor = nameColor;
            //it.ValueColor = valueColor;
            it.EvaluateStatusIconVisible = evaluateStatusIcon != null;
            it.EvaluateStatusIcon = evaluateStatusIcon;
            it.ValueButtonVisible = valueButton != null;
            it.ValueButtonText = valueButton;
            it.ViewerButtonVisible = showViewerButton;

            //if (ValidObjectForPreviewIcon(it))
              //  store.SetValue(it, PreviewIconColumn, "md-empty");

            if (parent == null && PinnedWatch != null)
            {
                /*store.SetValue(it, PinIconColumn, "md-pin-down");
                if (PinnedWatch.LiveUpdate)
                    store.SetValue(it, LiveUpdateIconColumn, liveIcon);
                else
                    store.SetValue(it, LiveUpdateIconColumn, noLiveIcon);*/
            }
            //if (RootPinAlwaysVisible && (!hasParent && PinnedWatch == null && AllowPinning))
              //  store.SetValue(it, PinIconColumn, "md-pin-up");

            if (hasChildren)
            {
                // Add dummy node
                it.Children.Add(new ObjectValueViewModel2 { Name = "Loading..." });
                if (!showExpanders)
                    showExpanders = true;
                valPath += "/";
                foreach (var oldPath in _oldValues.Keys)
                {
                    if (oldPath.StartsWith(valPath, StringComparison.Ordinal))
                    {
                        Console.WriteLine("TODO here we expand or collapse watchlistvm.cs");
                        //ExpandRow(store.GetPath(it), false);
                        break;
                    }
                }
            }
        }

        string pinnedWatch = null;
        public string PinnedWatch
        {
            get
            {
                return pinnedWatch;
            }
            set
            {
                if (pinnedWatch == value)
                    return;
                pinnedWatch = value;
                if (value == null)
                {
                    //pinCol.FixedWidth = 16;
                }
                else
                {
                    //pinCol.FixedWidth = 38;
                }
            }
        }

        string GetIterPath(ObjectValueViewModel2 iter)
        {
            var path = new StringBuilder();

            do
            {
                string name = iter.Name;
                path.Insert(0, "/" + name);
                iter = iter.Parent;
            } while (iter != null);

            return path.ToString();
        }

        bool allowAdding;
        public bool AllowAdding
        {
            get
            {
                return allowAdding;
            }
            set
            {
                allowAdding = value;
                Refresh(false);
            }
        }

        bool allowEditing;
        public bool AllowEditing
        {
            get
            {
                return allowEditing;
            }
            set
            {
                allowEditing = value;
                Refresh(false);
            }
        }

        public bool AllowPinning
        {
            get;set;
           // get { return pinCol.Visible; }
           // set { pinCol.Visible = value; }
        }

        void UnregisterValue(ObjectValue val)
        {
            val.ValueChanged -= OnValueUpdated;

            _nodes.Remove(val);
        }

        void OnValueUpdated(object o, EventArgs a)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed)
                    return;

                var val = (ObjectValue)o;
                ObjectValueViewModel2 it;

                if (_nodes.ContainsKey(val))
                {
                    it = _nodes[val];

                    // Keep the expression name entered by the user
                    //if (store.IterDepth(it) == 0)
                    //  val.Name = (string)store.GetValue(it, NameColumn);

                    //    RemoveChildren(it);
                    //    TreeIter parent;

                    //    if (!store.IterParent(out parent, it))
                    //        parent = TreeIter.Zero;

                    //    // If it was an evaluating group, replace the node with the new nodes
                    //    if (val.IsEvaluatingGroup)
                    //    {
                    //        if (val.ArrayCount == 0)
                    //        {
                    //            store.Remove(ref it);
                    //        }
                    //        else
                    //        {
                    //            SetValues(parent, it, null, val.GetArrayItem(0));
                    //            RegisterValue(val, it);
                    //            for (int n = 1; n < val.ArrayCount; n++)
                    //            {
                    //                it = store.InsertNodeAfter(it);
                    //                ObjectValue cval = val.GetArrayItem(n);
                    //                SetValues(parent, it, null, cval);
                    //                RegisterValue(cval, it);
                    //            }
                    //        }
                    //    }
                    //    else
                    //    {
                    //        SetValues(parent, it, val.Name, val);
                    //    }
                    //}
                    //UnregisterValue(val);
                    //if (compact)
                    //    RecalculateWidth();
                }
            });
        }

        public StackFrame Frame
        {
            get { return _frame; }
            set
            {
                _frame = value;
                Update();
            }
        }

        public void ClearValues()
        {
            _values.Clear();
            Refresh(true);
        }

        public void AddValues(ObjectValue[] newValues)
        {
            foreach(var val in newValues)
            {
                _values.Add(val);
            }

            Refresh(false);
        }
    }

    [ExportToolControl]
    [Export(typeof(IWatchList))]
    [Export(typeof(IExtension))]
    [Shared]
    public class LocalsViewModel2 : WatchListViewModel2
    {
        public override void OnUpdateList()
        {
            base.OnUpdateList();

            var frame = DebugManager.SelectedFrame;

            if(frame == null)
            {
                return;
            }

            Tree.ClearValues();
            Tree.AddValues(frame.GetAllLocals().Where(l => !string.IsNullOrWhiteSpace(l.Name) && l.Name != "?").ToArray());
        }
    }


    public class WatchListViewModel2 : ToolViewModel, IActivatableExtension, IWatchList
    {
        private bool _needsUpdate;
        private StackFrame _lastFrame;

        public override Location DefaultLocation => Location.Bottom;

        protected IDebugManager2 DebugManager { get; set; }

        public WatchListViewModel2()
        {
            Title = "Watch List 2.0";

            Tree = new WatchListTreeViewModel();
        }

        public WatchListTreeViewModel Tree { get; } = new WatchListTreeViewModel();

        public void Activation()
        {
            DebugManager = IoC.Get<IDebugManager2>();

            if (DebugManager != null)
            {
                DebugManager.FrameChanged += DebugManager_FrameChanged; ;
                DebugManager.DebugSessionStarted += (sender, e) => { IsVisible = true; };

                DebugManager.DebugSessionEnded += (sender, e) =>
                {
                    //IsVisible = false;
                    //Clear();
                };
            }

            IoC.Get<IStudio>().DebugPerspective.AddOrSelectTool(this);
        }

        private void DebugManager_FrameChanged(object sender, System.EventArgs e)
        {
            // todo if we are not visible then skip
            OnUpdateList();
            
        }

        public virtual void OnUpdateList()
        {
            _needsUpdate = false;

            if(DebugManager.SelectedFrame != _lastFrame)
            {
                Tree.Frame = DebugManager.SelectedFrame;
            }

            _lastFrame = DebugManager.SelectedFrame;
        }

        public void BeforeActivation()
        {
        }

        public void Add(ObjectValue value)
        {
        }

        public bool AddWatch(string expression)
        {
            return true;
        }

        public void Remove(ObjectValue value)
        {
        }
    }

    [ExportToolControl]
    [Export(typeof(IWatchList))]
    [Export(typeof(IExtension))]
    [Shared]
    public class WatchListViewModel : ToolViewModel, IActivatableExtension, IWatchList
    {
        protected IDebugManager2 DebugManager { get; set; }

        private readonly List<ObjectValue> watches;
        private List<string> _expressions;

        private ObservableCollection<ObjectValueViewModel> children;
        public List<ObjectValueViewModel> LastChangedRegisters { get; set; }

        public WatchListViewModel()
        {
            Dispatcher.UIThread.InvokeAsync(() => { IsVisible = false; });
            watches = new List<ObjectValue>();
            Title = "Watch List";
            Children = new ObservableCollection<ObjectValueViewModel>();
            LastChangedRegisters = new List<ObjectValueViewModel>();

            _expressions = new List<string>();


            AddExpressionCommand = ReactiveCommand.Create(() =>
            {
                AddWatch(Expression);
                Expression = "";
            });

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Activation(); // for when we create the part outside of composition.
            });
        }

        public ObservableCollection<ObjectValueViewModel> Children
        {
            get { return children; }
            set { this.RaiseAndSetIfChanged(ref children, value); }
        }

        public override Location DefaultLocation
        {
            get { return Location.Bottom; }
        }

        public virtual void BeforeActivation()
        {            
        }

        public virtual void Activation()
        {
            DebugManager = IoC.Get<IDebugManager2>();

            if (DebugManager != null)
            {
                DebugManager.FrameChanged += DebugManager_FrameChanged;
                DebugManager.DebugSessionStarted += (sender, e) => { IsVisible = true; };

                DebugManager.DebugSessionEnded += (sender, e) =>
                {
                    IsVisible = false;
                    Clear();
                };
            }

            IoC.Get<IStudio>().DebugPerspective.AddOrSelectTool(this);
        }

        private void DebugManager_FrameChanged(object sender, System.EventArgs e)
        {
            Task.Run(() =>
            {
                var expressions = DebugManager.SelectedFrame.GetExpressionValues(_expressions.ToArray(), false);

                //Dispatcher.UIThread.InvokeAsync(() =>
                //{
                InvalidateObjects(expressions);
                //});
            });
        }

        public void InvalidateObjects(params ObjectValue[] variables)
        {
            var updated = new List<ObjectValue>();
            var removed = new List<ObjectValue>();

            for (var i = 0; i < watches.Count; i++)
            {
                var watch = watches[i];

                var currentVar = variables.FirstOrDefault(v => v.Name == watch.Name);

                if (currentVar == null)
                {
                    removed.Add(watch);
                }
                else
                {
                    updated.Add(watch);
                }
            }

            foreach (var variable in variables)
            {
                var currentVar = updated.FirstOrDefault(v => v.Name == variable.Name);

                if (currentVar == null)
                {
                    watches.Add(variable);
                    Add(variable);
                }
                else
                {
                    var currentVm = Children.FirstOrDefault(c => c.Model.Name == currentVar.Name);

                    currentVm?.ApplyChange(variable);
                }
            }

            foreach (var removedvar in removed)
            {
                watches.Remove(removedvar);
                Remove(removedvar);
            }
        }

        public void Add(ObjectValue model)
        {
            var newWatch = new ObjectValueViewModel(this, model);

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Children.Add(newWatch);
            });
        }

        public void Remove(ObjectValue value)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Children.Remove(Children.FirstOrDefault(c => c.Model.Name == value.Name));
            });
        }

        private void ApplyChange(ObjectValue change)
        {
            foreach (var watch in Children)
            {
                if (watch.ApplyChange(change))
                {
                    break;
                }
            }
        }

        public virtual void Clear()
        {
            Children.Clear();

            watches.Clear();
        }

        public bool AddWatch(string expression)
        {
            bool result = false;

            if (!string.IsNullOrEmpty(expression))
            {
                if (DebugManager.SessionActive && !DebugManager.Session.IsRunning && DebugManager.Session.IsConnected && !_expressions.Contains(expression))
                {
                    _expressions.Add(expression);

                    var watch = DebugManager.SelectedFrame.GetExpressionValues(_expressions.ToArray(), false);

                    InvalidateObjects(watch);

                    return true;
                }
            }

            return result;
        }

        private string _expression = "";
        public string Expression
        {
            get { return _expression; }
            set { this.RaiseAndSetIfChanged(ref _expression, value); }
        }

        public ReactiveCommand AddExpressionCommand { get; }

    }
}
