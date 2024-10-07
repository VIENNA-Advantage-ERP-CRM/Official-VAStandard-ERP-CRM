/************************************************************
 * Module Name    : VAS
 * Purpose        : Created Widget to show top 10 expense
 *                  amount
 * chronological  : Development
 * Created Date   : 01 October 2024
 * Created by     : VIS_427
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_YearBasedExpenseData = function () {
        var widgetID = 0;
        this.frame;
        this.windowNo;
        this.widgetInfo;
        var $bsyDiv;
        var $self = this;
        var $maindiv = null;
        var vDifferentYearDataList = null;
        var $root = $('<div class="h-100 w-100 vas-ExAmount-background">');
        var ExpenseAmountData = [];
        var ExpenseNameArray = [];

        this.initalize = function () {
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) != 0 ? this.widgetInfo.AD_UserHomeWidgetID : $self.windowNo);
            GetColumnID();
            createBusyIndicator();
            $maindiv = $('<div id="vas_pieChartExpense_' + widgetID + '" class="vas-piechartexpense-container">');
            var MainHeadingComboDiv = $('<div class="d-flex justify-content-between vas-expam-div">');
            var HeadingDiv = $('<div class= "vas-t10exp-heading">' + VIS.Msg.getMsg("VAS_Top10Expenses") + '</div>');
            // YearBasedDataListDiv = $('<div class="VAS-YearBasedDataListDiv">');
            $DifferentYearDataListDiv = $('<div class="input-group vis-input-wrap">');
            /* parameters are: context, windowno., coloumn id, display type, DB coloumn name, Reference key, Is parent, Validation Code*/
            $DifferentYearDataListLookUp = VIS.MLookupFactory.get(VIS.Env.getCtx(), widgetID, 0, VIS.DisplayType.List, "VAS_DifferentYear", ColumnIds.AD_Reference_ID, false, null);
            // Parameters are: columnName, mandatory, isReadOnly, isUpdateable, lookup,display length
            vDifferentYearDataList = new VIS.Controls.VComboBox("VAS_DifferentYear", true, false, true, $DifferentYearDataListLookUp, 20);
            vDifferentYearDataList.setValue("1");
            var $DifferentYearDataListControlWrap = $('<div class="vis-control-wrap">');
            $DifferentYearDataListDiv.append($DifferentYearDataListControlWrap);
            $DifferentYearDataListControlWrap.append(vDifferentYearDataList.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' '));
            $DifferentYearDataListDiv.append($DifferentYearDataListControlWrap);
            MainHeadingComboDiv.append(HeadingDiv).append($DifferentYearDataListDiv);
            $maindiv.append(MainHeadingComboDiv);
            $root.append($maindiv)
            vDifferentYearDataList.fireValueChanged = function () {
                vDifferentYearDataList.setValue(vDifferentYearDataList.getValue());
                $bsyDiv[0].style.visibility = "visible";
                ExpenseAmountData = [];
                ExpenseNameArray = [];
                $maindiv.find('#vas_norecordcont_' + widgetID).remove();
                $self.getExpenseDetails();
            }
        };

        this.getExpenseDetails = function () {
            // Show busy indicator while data is loading
            $bsyDiv[0].style.visibility = "visible";

            // Define your AJAX request
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetTop10ExpenseAmountData", { "ListValue": vDifferentYearDataList.getValue() }, function (dr) {
                var ExpenseData = dr;

                // Remove existing canvas if exists
                $root.find('canvas').remove();

                if (ExpenseData.length > 0) {
                    for (var i = 0; i < ExpenseData.length; i++) {
                        if (ExpenseData[i].ExpenseAmount != 0) {
                            ExpenseAmountData.push(ExpenseData[i].ExpenseAmount);
                            ExpenseNameArray.push(ExpenseData[i].ExpenseName);
                        }
                    }

                    // Define static labels and colors
                    const labels = ExpenseNameArray;
                    const backgroundColors = [
                        'rgb(54, 162, 235, 0.6)',
                        'rgba(111, 66, 193, 0.5)',
                        'rgba(0, 187, 0, 0.5)',
                        'rgb(255, 205, 86)',
                        'rgb(255, 0, 0, 0.5)',
                        'rgba(255, 99, 132, 0.6)',  // Red
                        'rgba(75, 192, 192, 0.5)',  // Teal
                        'rgba(153, 102, 255, 0.5)', // Purple
                        'rgba(255, 159, 64, 0.5)',  // Orange
                        'rgb(124, 246, 0)'    // Light Green
                    ];

                    // Prepare the data object for the chart
                    const data = {
                        labels: labels, // Dynamic labels
                        datasets: [
                            {
                                backgroundColor: backgroundColors,
                                data: ExpenseAmountData,
                                borderColor: 'rgba(0,0,0,0)', // Transparent border color
                                borderWidth: 0 // No border
                            },
                        ],
                    };

                    // Define the chart configuration for Doughnut chart
                    const config = {
                        type: 'doughnut',
                        data: data,
                        options: {
                            responsive: true,
                            elements: {
                                arc: {
                                    borderWidth: 1 // Adjust the border width if needed
                                },
                                layout: {
                                    padding: {
                                        bottom: 0
                                    },
                                },
                            },
                            radius: '70%', // Adjust this value to decrease the outer radius
                            plugins: {
                                legend: {
                                    display: true,
                                    position: 'bottom', // Positioning the legend on the right
                                    labels: {
                                        generateLabels: function (chart) {
                                            const data = chart.data;
                                            const labels = data.labels;
                                            return labels.map((label, i) => ({
                                                text: label,
                                                fillStyle: backgroundColors[i] || '#000000',
                                                strokeStyle: 'transparent',
                                                lineWidth: 0
                                            }));
                                        },
                                        boxWidth: 10
                                    },
                                    padding: {
                                        top: 0
                                    },
                                },
                                tooltip: {
                                    callbacks: {
                                        label: function (tooltipItem) {
                                            const dataIndex = tooltipItem.dataIndex;
                                            const datasetIndex = tooltipItem.datasetIndex;
                                            const dataset = tooltipItem.chart.data.datasets[datasetIndex];
                                            const labels = tooltipItem.chart.data.labels;
                                            const value = dataset.data[dataIndex];
                                            return labels[dataIndex] + ': ' + value;
                                        }
                                    }
                                },
                                datalabels: {
                                    display: false,
                                    color: '#000',
                                    anchor: 'end',
                                    align: 'end',
                                    formatter: function (value) {
                                        return value; // Return value for external use only
                                    },
                                    font: {
                                        weight: 'bold'
                                    }
                                }
                            }
                        },
                        /*plugins: [
                            //doughnutLabelsLine = {
                            //    id: 'doughnutLabelsLine',
                            //    afterDraw: function (chart, args, options) {
                            //        const { ctx, chartArea } = chart;
                            //        const centerX = (chartArea.left + chartArea.right) / 2;
                            //        const centerY = (chartArea.top + chartArea.bottom) / 2;
                            //        const radius = Math.min(chartArea.width, chartArea.height) / 2;
                            //        const offset = 90;

                            //        let previousY = null;
                            //        let previousAngle = null;

                            //        chart.data.datasets.forEach((dataset, i) => {
                            //            if (!chart.isDatasetVisible(i)) return;

                            //            chart.getDatasetMeta(i).data.forEach((datapoint, index) => {
                            //                if (datapoint.hidden) return;

                            //                const { startAngle, endAngle } = datapoint;
                            //                let angle = (startAngle + endAngle) / 2;

                            //                if (previousAngle !== null && Math.abs(angle - previousAngle) < 0.01) {
                            //                    angle += 0.05;
                            //                }

                            //                const adjustedRadius = radius - offset;

                            //                const startX = centerX + adjustedRadius * Math.cos(angle);
                            //                const startY = centerY + adjustedRadius * Math.sin(angle);

                            //                const { x, y } = datapoint.tooltipPosition();
                            //                const lineLength = radius / 3.5;  // Shortened line length
                            //                let extraLine = 10;               // Shortened extra line length
                            //                const xLine = x >= centerX ? x + lineLength + 25 : x - lineLength - 25;
                            //                let yLine = y >= centerY ? y + lineLength - 25 : y - lineLength + 25;

                            //                const minSpacing = 20;
                            //                if (previousY !== null && Math.abs(yLine - previousY) < minSpacing) {
                            //                    yLine = previousY + (y >= centerY ? minSpacing : -minSpacing);
                            //                    extraLine += 5;               // Adjust extra line length as needed
                            //                }

                            //                previousY = yLine;
                            //                previousAngle = angle;

                            //                ctx.beginPath();
                            //                ctx.moveTo(startX, startY);
                            //                ctx.lineTo(xLine, yLine);
                            //                ctx.lineTo(xLine + (x >= centerX ? extraLine : -extraLine), yLine);
                            //                ctx.strokeStyle = 'rgba(0,0,0,0.6)';
                            //                ctx.lineWidth = 1;
                            //                ctx.stroke();
                            //                ctx.closePath();

                            //                ctx.fillStyle = 'rgba(0,0,0,0.6)';
                            //                ctx.font = '12px sans-serif';

                            //                const textAlign = x >= centerX ? 'left' : 'right';
                            //                const textX = x >= centerX ? xLine + extraLine + 5 : xLine - extraLine - 5;
                            //                const textY = yLine;

                            //                ctx.textAlign = textAlign;
                            //                ctx.textBaseline = 'middle';
                            //                ctx.fillText(dataset.data[index], textX, textY);
                            //            });
                            //        });
                            //    }
                            //}
                        ]*/
                    };

                    // Create a new canvas element and append it to the root
                    const canvas = $('<canvas></canvas>');
                    var polarChart = $root.find('#vas_pieChartExpense_' + widgetID);
                    polarChart.append(canvas);

                    // Initialize the chart with the new data
                    const ctx = canvas[0].getContext('2d');
                    new Chart(ctx, config);

                } else {
                    // Display "No data available" message
                    $maindiv.append('<div class="vas-igwidg-notfounddiv" id="vas_norecordcont_' + widgetID + '">' + VIS.Msg.getMsg("VAS_RecordNotFound") + '</div>')
                    $root.append($maindiv);
                }

                // Hide busy indicator
                $bsyDiv[0].style.visibility = "hidden";
            });
        };


        var GetColumnID = function () {
            ColumnIds = VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetColumnID", { "refernceName": "VAS_DifferentYear" }, null);
        }

        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $bsyDiv[0].style.visibility = "visible";
            $root.append($bsyDiv);
        };

        this.getRoot = function () {
            return $root;
        };

        this.refreshWidget = function () {
            $bsyDiv[0].style.visibility = "visible";
            ExpenseAmountData = [];
            ExpenseNameArray = [];
            $maindiv.find('#vas_norecordcont_' + widgetID).remove();
            $self.getExpenseDetails();
        };
    };

    VAS.VAS_YearBasedExpenseData.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var self = this;
        window.setTimeout(function () {
            self.getExpenseDetails();
        }, 50);
    };

    VAS.VAS_YearBasedExpenseData.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_YearBasedExpenseData.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_YearBasedExpenseData.prototype.dispose = function () {
        this.frame = null;
        this.windowNo = null;
        $bsyDiv = null;
        $self = null;
        $root = null;
    };

})(VAS, jQuery);