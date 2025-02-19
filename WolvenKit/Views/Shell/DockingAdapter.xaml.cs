using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using ReactiveUI;
using Splat;
using Syncfusion.Windows.Tools.Controls;
using WolvenKit.App.ViewModels;
using WolvenKit.Functionality.Layout;
using WolvenKit.Functionality.Services;
using WolvenKit.Functionality.WKitGlobal.Helpers;
using WolvenKit.Interaction;
using WolvenKit.Models.Docking;
using WolvenKit.ViewModels.Documents;
using WolvenKit.ViewModels.Shell;
using WolvenKit.ViewModels.Tools;
using DockState = WolvenKit.Models.Docking.DockState;

namespace WolvenKit.Views.Shell
{
    //https://github.com/SyncfusionExamples/working-with-wpf-docking-manager-and-mvvm

    /// <summary>
    /// Interaction logic for DockingAdapter.xaml
    /// </summary>
    public partial class DockingAdapter : UserControl
    {
        private AppViewModel viewModel;
        public static DockingAdapter G_Dock;
        private bool UsingProjectLayout = false;
        private bool _hadLoadedProject = false;

        private Window _mainWindow;
        private bool _stateChanged;

        public DockingAdapter()
        {
            InitializeComponent();
            G_Dock = this;

            viewModel = DataContext as AppViewModel;
        }

        private void DockingAdapterOnLoaded(object sender, RoutedEventArgs e)
        {
            _mainWindow = Window.GetWindow(this);
            if (_mainWindow != null)
            {
                _mainWindow.Deactivated += OnMainWindowDeactivated;
                _mainWindow.Closing += OnMainWindowClosing;
            }
        }

        private void OnMainWindowDeactivated(object sender, EventArgs e)
        {
            if (_stateChanged)
            {
                foreach (Window win in Application.Current.Windows)
                {
                    if (win is NativeFloatWindow && win.Owner != null)
                    {
                        win.Owner = null;
                    }
                }
            }
            _stateChanged = false;
        }

        private void OnMainWindowClosing(object sender, CancelEventArgs e)
        {
            foreach (Window win in Application.Current.Windows)
            {
                if (win is NativeFloatWindow)
                {
                    win.Close();
                }
            }
        }

        private void PART_DockingManagerOnDockStateChanging(FrameworkElement sender, DockStateChangingEventArgs e)
        {
            if (e.SourceElement is ContentControl { Content: PaneViewModel vm })
            {
                vm.State = e.TargetState.ToDockState();
            }
        }

        private void PART_DockingManagerOnDockStateChanged(FrameworkElement sender, DockStateEventArgs e)
        {
            if (e.NewState == Syncfusion.Windows.Tools.Controls.DockState.Float)
            {
                _stateChanged = true;
            }
        }

        /// <summary>
        /// fires when a document (but no tool window) gets closed through clicking the close button
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void PART_DockingManagerOnCloseButtonClick(object sender, CloseButtonEventArgs e)
        {
            if (e.TargetItem is ContentControl { Content: DocumentViewModel vm })
            {
                e.Cancel = !await TryCloseDocument(vm);
            }
        }

        /// <summary>
        /// For windows in Float, Dock and AutoHidden state
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PART_DockingManager_WindowClosing(object sender, WindowClosingEventArgs e)
        {

        }

        private readonly bool _debuggingLayouts = false;
        private readonly bool _useAppdataStorage = true;

        public void SaveLayout()
        {
            if (_useAppdataStorage)
            {
                var xmlPath = Path.Combine(ISettingsManager.GetAppData(), "DockStates.xml");
                var writer = XmlWriter.Create(xmlPath);

                PART_DockingManager.SaveDockState(writer);

                writer.Close();
            }
            else
            {
                PART_DockingManager.SaveDockState();
            }
        }

        public void SaveLayoutToProject()
        {
            if (viewModel is null || viewModel.ActiveProject is null)
            {
                return;
            }

            var layoutPath = Path.Combine(viewModel.ActiveProject.ProjectDirectory, "layout.xml");
            var writer = XmlWriter.Create(layoutPath);
            PART_DockingManager.SaveDockState(writer);
            writer.Close();
        }

        private void LoadLayout()
        {
            try
            {
                if (_useAppdataStorage)
                {
                    var xmlPath = Path.Combine(ISettingsManager.GetAppData(), "DockStates.xml");
                    if (!File.Exists(xmlPath))
                    {
                        LoadLayoutDefault();
                        return;
                    }

                    var reader = XmlReader.Create(xmlPath);

                    var Debugging_A = PART_DockingManager.LoadDockState(reader);

                    reader.Close();
                }
                else
                {
                    var Debugging_A = PART_DockingManager.LoadDockState();
                    Trace.WriteLine(Debugging_A);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
                LoadLayoutDefault();
            }
        }

        public void LoadLayoutDefault()
        {
            try
            {
                var reader = XmlReader.Create("DockStatesDefault.xml");
                var Debugging_A = PART_DockingManager.LoadDockState(reader);
                Trace.WriteLine(Debugging_A);
                reader.Close();
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }
        }

        private void PART_DockingManager_Loaded(object sender, RoutedEventArgs e)
        {
            ((DocumentContainer)PART_DockingManager.DocContainer).SetCurrentValue(DocumentContainer.AddTabDocumentAtLastProperty, true);

            // Add setting to persist State or not ? ( Load Default Docking on Startup : Yes/No )
            // if (XSETTINGX){ SetLayoutToDefault();}else{
            var settings = Locator.Current.GetService<ISettingsManager>();
            if (settings != null && !settings.IsHealthy())
            {
                LoadLayoutDefault();
            }
            else
            {
                LoadLayout();
            }

            if (_debuggingLayouts)
            {
                SaveLayout();
            }

            // update the vms
            foreach (FrameworkElement frameworkElement in PART_DockingManager.Children)
            {
                if (frameworkElement is ContentControl contentControl)
                {
                    if (contentControl.Content is PaneViewModel vm)
                    {
                        vm.State = DockingManager.GetState(contentControl).ToDockState();
                    }
                }
            }

            viewModel ??= DataContext as AppViewModel;

            SizeChanged += Window_SizeChanged;
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!UsingProjectLayout)
            {
                SaveLayout();
            }
        }

        public IDocumentViewModel ActiveDocument
        {
            get => (IDocumentViewModel)GetValue(ActiveDocumentProperty);
            set => SetValue(ActiveDocumentProperty, value);
        }

        // Using a DependencyProperty as the backing store for ActiveDocument.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ActiveDocumentProperty =
            DependencyProperty.Register(nameof(ActiveDocument), typeof(IDocumentViewModel), typeof(DockingAdapter), new PropertyMetadata(null, new PropertyChangedCallback(OnActiveDocumentChanged)));

        public object ItemsSource
        {
            get => GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        // Using a DependencyProperty as the backing store for ItemsSource.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(object), typeof(DockingAdapter), new PropertyMetadata(null));

        private static void OnActiveDocumentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            if (sender is not DockingAdapter adapter)
            {
                return;
            }

            foreach (FrameworkElement element in adapter.PART_DockingManager.Children)
            {
                if (element is not ContentControl control)
                {
                    continue;
                }

                if (control.Content != args.NewValue)
                {
                    continue;
                }

                adapter.PART_DockingManager.SetCurrentValue(DockingManager.ActiveWindowProperty, control);

                adapter.viewModel?.UpdateTitle();

                break;
            }
        }
        public void OnActiveProjectChanged()
        {
            if (viewModel is null || viewModel.ActiveProject is null)
            {
                return;
            }

            try
            {
                // need to also handle if files have been modified (probably elsewhere, though)
                if (!_hadLoadedProject && ItemsSource is ObservableCollection<IDockElement> oc)
                {
                    _hadLoadedProject = true;
                    oc.Clear();
                }
                var layoutPath = Path.Combine(viewModel.ActiveProject.ProjectDirectory, "layout.xml");
                if (File.Exists(layoutPath))
                {
                    var reader = XmlReader.Create(layoutPath);
                    var Debugging_A = PART_DockingManager.LoadDockState(reader);
                    Trace.WriteLine(Debugging_A);
                    reader.Close();
                    UsingProjectLayout = true;
                    //PART_DockingManager.SetCurrentValue(DockingManager.PersistStateProperty, false);
                }
            }
            catch (Exception)
            {
                //viewModel.Log(e.Message);
                throw;
            }
        }


        /// <summary>
        /// This happens on the very first tool window assignments
        /// </summary>
        /// <param name="e"></param>
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            if (e.Property.Name == "ItemsSource")
            {
                if (e.OldValue != null)
                {
                    var oldcollection = e.OldValue as INotifyCollectionChanged;
                    oldcollection.CollectionChanged -= CollectionChanged;

                    //unsubscribe?
                }

                if (e.NewValue != null)
                {
                    ((DocumentContainer)PART_DockingManager.DocContainer).SetCurrentValue(DocumentContainer.AddTabDocumentAtLastProperty, true);

                    var newcollection = e.NewValue as INotifyCollectionChanged;

                    var count = 0;
                    foreach (var item in (IList)e.NewValue)
                    {
                        if (item is IDockElement dockElement)
                        {
                            // use normal events here?
                            dockElement.ObservableForProperty(x => x.State)
                                .ObserveOn(RxApp.MainThreadScheduler)
                                .Subscribe(OnStateUpdated);

                            // add control
                            var control = new ContentControl()
                            {
                                Content = item
                            };
                            DockingManager.SetHeader(control, dockElement.Header);
                            DockingManager.SetSideInDockedMode(control, (Syncfusion.Windows.Tools.Controls.DockSide)(int)dockElement.SideInDockedMode);
                            DockingManager.SetState(control, dockElement.State.ToSfDockState());
                            if (dockElement.State != DockState.Document)
                            {
                                if (count != 0)
                                {
                                    //DockingManager.SetTargetNameInDockedMode(control, "item" + (count - 1).ToString());
                                }
                                control.Name = "item" + count++.ToString();
                            }

                            PART_DockingManager.Children.Add(control);
                        }
                    }

                    newcollection.CollectionChanged += CollectionChanged;
                }
            }
            base.OnPropertyChanged(e);
        }

        /// <summary>
        /// For all programmatically added windows
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // remove windows
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    // add control
                    var control = (from ContentControl element in PART_DockingManager.Children
                                   where element.Content == item
                                   select element).FirstOrDefault();
                    PART_DockingManager.Children.Remove(control);

                    // set active document to null
                    if (control.Content is IDocumentViewModel document)
                    {
                        if (ActiveDocument == document)
                        {
                            SetCurrentValue(ActiveDocumentProperty, null);
                        }
                    }

                    // unsubscribe ?
                }
            }

            // add windows
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is IDockElement element)
                    {
                        // use normal events here?
                        element.ObservableForProperty(x => x.Header)
                            .ObserveOn(RxApp.MainThreadScheduler)
                            .Subscribe(OnHeaderChanged);
                        element.ObservableForProperty(x => x.State)
                            .ObserveOn(RxApp.MainThreadScheduler)
                            .Subscribe(OnStateUpdated);

                        // add control
                        var control = new ContentControl()
                        {
                            Content = element
                        };

                        // floating windows need size and positioning
                        if (item is FloatingPaneViewModel vm)
                        {
                            DockingManager.SetDesiredHeightInFloatingMode(control, vm.Height);
                            DockingManager.SetDesiredWidthInFloatingMode(control, vm.Width);
                            DockingManager.SetFloatingWindowRect(control, new Rect(400, 400, vm.Width, vm.Height));
                        }

                        DockingManager.SetHeader(control, element.Header);
                        DockingManager.SetState(control, element.State.ToSfDockState());
                        PART_DockingManager.Children.Add(control);
                    }
                }
            }
        }

        private void OnHeaderChanged(IObservedChange<IDockElement, string> headerChange)
        {
            var item = headerChange.Sender;
            var newHeader = headerChange.Value;

            var control = (from ContentControl element in PART_DockingManager.Children
                           where element.Content == item
                           select element).FirstOrDefault();

            var header = DockingManager.GetHeader(control) as string;

            if (header is string headerStr && !headerStr.Equals(newHeader))
            {
                DockingManager.SetHeader(control, newHeader);
            }
        }

        private void OnStateUpdated(IObservedChange<IDockElement, DockState> dockStateChange)
        {
            var item = dockStateChange.Sender;
            var control = (from ContentControl element in PART_DockingManager.Children
                           where element.Content == item
                           select element).FirstOrDefault();

            var newstate = dockStateChange.Value;
            // actually remove and not hide FloatingPaneViewModels
            if (control is ContentControl { Content: FloatingPaneViewModel vm } && newstate == DockState.Hidden)
            {
                viewModel.DockedViews.Remove(vm);
                return;
            }

            var dockstate = DockingManager.GetState(control).ToDockState();
            if (dockstate != newstate)
            {
                DockingManager.SetState(control, newstate.ToSfDockState());
            }
        }

        public DataTemplate FindDataTemplate(Type type, FrameworkElement element)
        {
            if (element.TryFindResource(type) is DataTemplate dataTemplate)
            {
                return dataTemplate;
            }

            if (type.BaseType != null && type.BaseType != typeof(object))
            {
                dataTemplate = FindDataTemplate(type.BaseType, element);
                if (dataTemplate != null)
                {
                    return dataTemplate;
                }
            }

            foreach (var interfaceType in type.GetInterfaces())
            {
                dataTemplate = FindDataTemplate(interfaceType, element);
                if (dataTemplate != null)
                {
                    return dataTemplate;
                }
            }

            return null;
        }

        private void PART_DockingManager_ActiveWindowChanged_1(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // set active property
            if (e.OldValue is ContentControl oldValue)
            {
                if (oldValue.Content is IDockElement dockElement)
                {
                    dockElement.IsActive = false;
                }
            }

            if (e.NewValue is ContentControl content)
            {
                if (content.Content is IDockElement dockElement)
                {
                    dockElement.IsActive = true;
                }

                var propertiesViewModel = Locator.Current.GetService<PropertiesViewModel>();
                if (content.Content is ProjectExplorerViewModel pevm)
                {
                    //propertiesViewModel.SetToNullAndResetVisibility();
                    propertiesViewModel.PE_FileInfoVisible = true;
                    propertiesViewModel.AB_FileInfoVisible = false;
                    //propertiesViewModel.PE_SelectedItem = pevm.SelectedItem;
                    propertiesViewModel.ExecuteSelectFile(pevm.SelectedItem);
                }
                else if (content.Content is AssetBrowserViewModel abvm)
                {
                    //propertiesViewModel.SetToNullAndResetVisibility();
                    propertiesViewModel.AB_FileInfoVisible = true;
                    propertiesViewModel.PE_FileInfoVisible = false;
                    //propertiesViewModel.AB_SelectedItem = abvm.RightSelectedItem;
                    propertiesViewModel.ExecuteSelectFile(abvm.RightSelectedItem);
                }

                if (content.Content != null)
                {
                    DiscordHelper.SetDiscordRPCStatus(content.Content as string);
                }

                //if (((IDockElement)content.Content).State == DockState.Document)
                try
                {
                    if (content.Content is IDocumentViewModel document)
                    {
                        SetCurrentValue(ActiveDocumentProperty, document);
                    }
                }
                catch (Exception)
                {
                }

            }

            viewModel?.UpdateTitle();
        }

        private async void PART_DockingManager_OnCloseAllTabs(object sender, CloseTabEventArgs e)
        {
            foreach (var item in e.ClosingTabItems)
            {
                if (item is TabItemExt { Content: ContentPresenter { Content: ContentControl { Content: DocumentViewModel vm } } })
                {
                    e.Cancel = !await TryCloseDocument(vm);
                }
            }
        }

        private async void PART_DockingManager_OnCloseOtherTabs(object sender, CloseTabEventArgs e)
        {
            foreach (var item in e.ClosingTabItems)
            {
                if (item is TabItemExt { Content: ContentPresenter { Content: ContentControl { Content: DocumentViewModel vm } } })
                {
                    e.Cancel = !await TryCloseDocument(vm);
                }
            }
        }

        private async Task<bool> TryCloseDocument(DocumentViewModel vm)
        {
            if (vm.IsDirty)
            {
                if (await Interactions.ShowMessageBoxAsync("Unsaved changes will be lost - are you sure you want to close this file?", "Confirm", WMessageBoxButtons.YesNo) == WMessageBoxResult.No)
                {
                    return false;
                }
            }

            vm.Close.Execute().Subscribe();

            (ItemsSource as IList).Remove(vm);
            viewModel.UpdateTitle();

            return true;
        }


    }
}
