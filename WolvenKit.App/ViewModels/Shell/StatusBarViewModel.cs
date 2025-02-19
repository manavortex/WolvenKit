using System;
using System.Reactive.Linq;
using System.Windows.Media;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using WolvenKit.Core.Interfaces;
using WolvenKit.Core.Services;
using WolvenKit.Functionality.Helpers;
using WolvenKit.Functionality.Services;

namespace WolvenKit.ViewModels.Shell
{
    public class StatusBarViewModel : ReactiveObject
    {
        #region Fields

        private const string s_noProjectLoaded =
            "NO PROJECT LOADED | Create a New Project or Open an existing Project to get started with WolvenKit";

        public ISettingsManager _settingsManager { get; set; }
        private readonly ILoggerService _loggerService;

        private readonly IProjectManager _projectManager;
        private readonly IProgressService<double> _progressService;

        private readonly ObservableAsPropertyHelper<double> _progress;

        private readonly ObservableAsPropertyHelper<string> _currentProject;

        #endregion Fields

        #region Constructors

        public StatusBarViewModel(
            ISettingsManager settingsManager,
            IProjectManager projectManager,
            ILoggerService loggerService,
            IProgressService<double> progressService
            )
        {
            _settingsManager = settingsManager;
            _projectManager = projectManager;
            _progressService = progressService;
            _loggerService = loggerService;

            IsLoading = false;
            LoadingString = "";


            _projectManager
                .WhenAnyValue(
                    x => x.ActiveProject,
                    project => project != null ? project.Name : s_noProjectLoaded)
                .ToProperty(
                    this,
                    x => x.CurrentProject,
                    out _currentProject);

            _ = Observable.FromEventPattern<EventHandler<double>, double>(
                handler => _progressService.ProgressChanged += handler,
                handler => _progressService.ProgressChanged -= handler)
                .Select(_ => _.EventArgs * 100)
                .ToProperty(this, x => x.Progress, out _progress);

            _ = _progressService.WhenAnyValue(x => x.IsIndeterminate).Subscribe(b =>
            {
                DispatcherHelper.RunOnMainThread(() =>
                {
                    IsIndeterminate = b;
                });
            });
            _ = _progressService.WhenAnyValue(x => x.Status).Subscribe(s =>
            {
                DispatcherHelper.RunOnMainThread(() =>
                {
                    Status = s.ToString();
                    switch (s)
                    {
                        case EStatus.Running:
                            BarColor = Brushes.DarkOrange;
                            break;
                        case EStatus.Ready:
                            BarColor = (SolidColorBrush)new BrushConverter().ConvertFromString("#951C2D");
                            break;
                        default:
                            break;
                    }
                });

            });
        }

        #endregion Constructors

        #region Properties

        public double Progress => _progress.Value;

        [Reactive] public bool IsIndeterminate { get; set; }

        public string InternetConnected { get; private set; }

        public bool IsLoading { get; set; }

        public string LoadingString { get; set; }

        public string CurrentProject => _currentProject.Value;

        [Reactive] public string Status { get; set; } = "Ready";

        public object VersionNumber => _settingsManager.GetVersionNumber();


        [Reactive] public Brush BarColor { get; set; } = Brushes.Black;

        #endregion Properties

    }
}
