﻿/*
 * Original author: Nicholas Shulman <nicksh .at. u.washington.edu>,
 *                  MacCoss Lab, Department of Genome Sciences, UW
 *
 * Copyright 2014 University of Washington - Seattle, WA
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System.Windows.Forms;
using pwiz.Common.DataBinding;
using pwiz.Common.DataBinding.Controls;
using pwiz.Skyline.Model.Databinding.Entities;

namespace pwiz.Skyline.Controls.Databinding
{
    /// <summary>
    /// Subclass of BoundDataGridView which exposes some test methods.
    /// </summary>
    public class BoundDataGridViewEx : BoundDataGridView
    {
        /// <summary>
        /// Testing method: Sends Ctrl-V to this control.
        /// </summary>
        public void SendPaste()
        {
            OnKeyDown(new KeyEventArgs(Keys.V | Keys.Control));
        }

        public void SendKeyDownUp(KeyEventArgs keyEventArgs)
        {
            OnKeyDown(keyEventArgs);
            OnKeyUp(keyEventArgs);
        }

        protected override void OnCellErrorTextNeeded(DataGridViewCellErrorTextNeededEventArgs e)
        {
            var column = Columns[e.ColumnIndex];
            var bindingSource = DataSource as BindingListSource;
            if (bindingSource != null)
            {
                var propertyDescriptor =
                    bindingSource.FindDataProperty(column.DataPropertyName) as ColumnPropertyDescriptor;
                var parentColumn = propertyDescriptor?.DisplayColumn?.ColumnDescriptor?.Parent;
                if (parentColumn == null || !typeof(IErrorTextProvider).IsAssignableFrom(parentColumn.PropertyType))
                {
                    return;
                }

                var parentValue = parentColumn.GetPropertyValue((RowItem)bindingSource[e.RowIndex], null) as IErrorTextProvider;
                if (parentValue != null)
                {
                    e.ErrorText = parentValue.GetErrorText(propertyDescriptor.DisplayColumn.PropertyPath.Name);
                }

            }
            base.OnCellErrorTextNeeded(e);
        }
    }
}
