﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using EnvDTE80;
using Microsoft.VisualStudio.Imaging;
using SmartCmdArgs.Helper;
using SmartCmdArgs.View.Converter;
using SmartCmdArgs.ViewModel;

namespace SmartCmdArgs.View
{
    /// <summary>
    /// Interaction logic for ArgumentItemView.xaml
    /// </summary>
    public partial class ArgumentItemView : UserControl
    {
        private CmdBase Item => (CmdBase)DataContext;

        public ArgumentItemView()
        {
            InitializeComponent();

            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            BindingOperations.ClearBinding(Icon, CrispImage.MonikerProperty);
            BindingOperations.ClearBinding(ItemTagText, Run.TextProperty);
            BindingOperations.ClearBinding(ItemTag, Border.VisibilityProperty);

            if (e.OldValue is CmdBase oldCmdBase)
            {
                WeakEventManager<CmdBase, CmdBase.EditModeChangedEventArgs>.RemoveHandler(oldCmdBase, nameof(CmdBase.EditModeChanged), OnItemEditModeChanged);

                oldCmdBase.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (e.NewValue is CmdBase cmdBase)
            {
                WeakEventManager<CmdBase, CmdBase.EditModeChangedEventArgs>.AddHandler(cmdBase, nameof(CmdBase.EditModeChanged), OnItemEditModeChanged);

                cmdBase.PropertyChanged += OnViewModelPropertyChanged;

                UpdateRedBorderOverlay(cmdBase);

                if (cmdBase is CmdContainer con)
                {
                    MultiBinding bind = new MultiBinding
                    {
                        Mode = BindingMode.OneWay,
                        Converter = new ItemMonikerConverter()
                    };
                    bind.Bindings.Add(new Binding { Source = con });
                    bind.Bindings.Add(new Binding
                    {
                        Source = con,
                        Path = new PropertyPath(nameof(CmdContainer.IsExpanded))
                    });
                    Icon.SetBinding(CrispImage.MonikerProperty, bind);
                }

                if (cmdBase is CmdArgument arg)
                {
                    var itemTagTextBinding = new Binding {
                        Source = arg,
                        Path = new PropertyPath(nameof(CmdArgument.ArgumentType)),
                        Converter = new ItemTagTextConverter(),
                    };
                    ItemTagText.SetBinding(Run.TextProperty, itemTagTextBinding);

                    var toolWindow = TreeHelper.FindAncestorOrSelf<ToolWindowControl>(this);

                    var itemTagVisibilityBinding = new MultiBinding
                    {
                        Mode = BindingMode.OneWay,
                        Converter = new ItemTagVisibilityConverter(),
                        Bindings =
                        {
                            new Binding {
                                Source = arg,
                                Path = new PropertyPath(nameof(CmdArgument.ArgumentType)),
                            },
                            new Binding {
                                Source = toolWindow.DataContext,
                                Path = new PropertyPath(nameof(ToolWindowViewModel.DisplayTagForCla)),
                            },
                        }
                    };
                    ItemTag.SetBinding(Border.VisibilityProperty, itemTagVisibilityBinding);
                }
                else
                {
                    ItemTag.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is CmdBase cmdBase)
                UpdateRedBorderOverlay(cmdBase);
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is CmdArgument cmdArg)
            {
                if (e.PropertyName == nameof(CmdArgument.Value)
                    || e.PropertyName == nameof(CmdArgument.ArgumentType)
                    || e.PropertyName == nameof(CmdArgument.IsInEditMode))
                {
                    UpdateRedBorderOverlay(cmdArg);
                }
            }
        }

        private RedBorderAdorner _redBorderAdorner;

        public void UpdateRedBorderOverlay(CmdBase cmdBase)
        {
            var adornerLayer = AdornerLayer.GetAdornerLayer(MainPanel);
            if (adornerLayer == null)
                return;

            var shouldShow = false;
            if (cmdBase is CmdArgument cmdArg && cmdArg.ArgumentType == ArgumentType.EnvVar)
            {
                shouldShow = !cmdArg.IsInEditMode && !string.IsNullOrEmpty(cmdArg.Value) && !cmdArg.Value.Contains('=');
            }

            var isShown = _redBorderAdorner != null;
            if (shouldShow != isShown)
            {
                if (shouldShow)
                {
                    _redBorderAdorner = new RedBorderAdorner(MainPanel);
                    adornerLayer.Add(_redBorderAdorner);
                }
                else
                {
                    adornerLayer.Remove(_redBorderAdorner);
                    _redBorderAdorner = null;
                }
            }
        }

        private void OnItemEditModeChanged(object sender, CmdBase.EditModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case CmdBase.EditMode.BeganEdit:
                    EnterEditMode(selectAll: true);
                    break;
                case CmdBase.EditMode.BeganEditAndReset:
                    EnterEditMode(selectAll: false);
                    break;
                case CmdBase.EditMode.CanceledEdit:
                    LeaveEditMode(editCanceled: true);
                    break;
                case CmdBase.EditMode.CommitedEdit:
                    LeaveEditMode(editCanceled: false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(e), e, null);
            }
        }

        private void EnterEditMode(bool selectAll)
        {
            textbox.Text = Item.Value;

            textblock.Visibility = Visibility.Collapsed;
            textbox.Visibility = Visibility.Visible;
            textbox.Focus();

            if (selectAll)
                textbox.SelectAll();
            else
                textbox.CaretIndex = textbox.Text.Length;
        }

        private void LeaveEditMode(bool editCanceled)
        {
            if (!editCanceled)
            {
                Item.Value = textbox.Text;
            }

            textbox.Visibility = Visibility.Collapsed;
            textblock.Visibility = Visibility.Visible;
        }

        private void Textbox_OnKeyDown(object sender, KeyEventArgs e)
        {
            // Escape is a CmdKey and hence handled in ToolWindow

            if (e.Key == Key.Return && Item.IsInEditMode)
            {
                Item.CommitEdit();
                e.Handled = true;
            }
        }


        private int OldSelectionStart = -1;
        private int OldSelectionEnd = -1;

        private void textbox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (Item.IsInEditMode)
            {
                Item.CommitEdit();
            }

            OldSelectionStart = -1;
            OldSelectionEnd = -1;
        }


        private static double ScrollOffset = 5;
        private void Textbox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            int index = -1;

            // check if Selection end or start have changed and the changed one as the index to scroll to.
            if (OldSelectionEnd != textbox.SelectionStart + textbox.SelectionLength)
                index = OldSelectionEnd = textbox.SelectionStart + textbox.SelectionLength;
            if (OldSelectionStart != textbox.SelectionStart)
                index = OldSelectionStart = textbox.SelectionStart;

            // if neither start or end have changed, keep scroll position
            if (index < 0)
                return;

            // on the first edit on a line the rect will be empty
            var rect = textbox.GetRectFromCharacterIndex(index);
            if (rect == Rect.Empty)
                return;

            // get point to scroll to relative to the TreeViewItem to include the indent
            var treeViewItem = TreeHelper.FindAncestorOrSelf<TreeViewItemEx>(this);
            var point = textbox.TranslatePoint(rect.TopLeft, treeViewItem);
            
            var sv = TreeHelper.FindAncestorOrSelf<ScrollViewer>(treeViewItem);

            // if the scroll offset to large, so the point we havt to make visible is left off screen we scroll left
            if (sv.HorizontalOffset > point.X - ScrollOffset)
            {
                sv.ScrollToHorizontalOffset(point.X - ScrollOffset);
            }
            // if the scroll offset to small, so the point we havt to make visible is right off screen we scroll right
            else if (sv.HorizontalOffset + sv.ViewportWidth < point.X + ScrollOffset)
            {
                sv.ScrollToHorizontalOffset(point.X - sv.ViewportWidth + ScrollOffset);
            }
        }
    }
}
