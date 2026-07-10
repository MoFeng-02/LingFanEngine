using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LingFanEngine.SDK.ViewModels;
using MFToolkit.Routing.Core.Interfaces;

namespace LingFanEngine.SDK.Views.Pages;

/// <summary>项目管理页面</summary>
public class ProjectPage : UserControl
{
    private readonly ProjectViewModel _viewModel;
    private ListBox? _projectList;

    public ProjectPage(ProjectViewModel viewModel, IRouter router)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        // 创建成功后自动跳转到编辑器
        _viewModel.ProjectOpened += async () =>
        {
            await router.NavigateAsync("/editor");
        };

        InitializeComponent();
    }

    private void InitializeComponent()
    {
        var grid = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,Auto,*,Auto"),
            Margin = new Thickness(16),
        };

        // ===== 标题 =====
        grid.Children.Add(new TextBlock
        {
            Text = "项目管理",
            Classes = { "page-title" },
        });

        // ===== 创建项目区域 =====
        var createPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 16),
        };
        Grid.SetRow(createPanel, 1);

        var nameBox = new TextBox { PlaceholderText = "项目名（英文）", Width = 150 };
        nameBox.Text = _viewModel.NewProjectName;
        nameBox.TextChanged += (_, _) => _viewModel.NewProjectName = nameBox.Text;

        var titleBox = new TextBox { PlaceholderText = "中文标题", Width = 150 };
        titleBox.Text = _viewModel.NewProjectTitle;
        titleBox.TextChanged += (_, _) => _viewModel.NewProjectTitle = titleBox.Text;

        var authorBox = new TextBox { PlaceholderText = "作者", Width = 100 };
        authorBox.Text = _viewModel.NewProjectAuthor;
        authorBox.TextChanged += (_, _) => _viewModel.NewProjectAuthor = authorBox.Text;

        var createBtn = new Button
        {
            Content = "新建项目",
            Command = _viewModel.CreateProjectCommand,
        };

        // 打开项目按钮
        var openBtn = new Button
        {
            Content = "打开项目...",
            Command = _viewModel.OpenProjectFileDialogCommand,
        };

        createPanel.Children.Add(nameBox);
        createPanel.Children.Add(titleBox);
        createPanel.Children.Add(authorBox);
        createPanel.Children.Add(createBtn);
        createPanel.Children.Add(openBtn);
        grid.Children.Add(createPanel);

        // ===== 最近项目列表 =====
        _projectList = new ListBox
        {
            Margin = new Thickness(0, 0, 0, 8),
        };

        var itemTemplate = new FuncDataTemplate<Models.RecentProject>((item, _) =>
            new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(4, 4),
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = item?.Name ?? "",
                                FontWeight = FontWeight.Bold,
                                FontSize = 14,
                            },
                            new TextBlock
                            {
                                Text = item?.LastOpened.ToString("yyyy-MM-dd") ?? "",
                                Foreground = Brushes.Gray,
                                FontSize = 11,
                                VerticalAlignment = VerticalAlignment.Center,
                            },
                        }
                    },
                    new TextBlock
                    {
                        Text = item?.Path ?? "",
                        Foreground = Brushes.Gray,
                        FontSize = 11,
                    },
                }
            });
        _projectList.ItemTemplate = itemTemplate;
        _projectList.ItemsSource = _viewModel.RecentProjects;

        // 双击打开项目
        _projectList.DoubleTapped += async (_, _) =>
        {
            if (_projectList.SelectedItem is Models.RecentProject selected)
            {
                await _viewModel.OpenProjectCommand.ExecuteAsync(selected.Path);
            }
        };

        Grid.SetRow(_projectList, 2);
        grid.Children.Add(_projectList);

        // ===== 状态栏 =====
        var statusText = new TextBlock
        {
            FontSize = 12,
            Foreground = Brushes.Gray,
        };
        // AOT 安全：手动监听 StatusMessage
        statusText.Text = _viewModel.StatusMessage;
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProjectViewModel.StatusMessage))
                statusText.Text = _viewModel.StatusMessage;
        };
        Grid.SetRow(statusText, 3);
        grid.Children.Add(statusText);

        Content = grid;
    }
}
