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
        var $root = $('<div class="h-100 w-100 vas-t10exp-background">');
        var ExpenseAmountData = [];
        var ExpenseNameArray = [];

        this.initalize = function () {
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) != 0 ? this.widgetInfo.AD_UserHomeWidgetID : $self.windowNo);
            GetColumnID();
            createBusyIndicator();
            $maindiv = $('<div id="vas_pieChartExpense_' + widgetID + '" class="vas-piechartexpense-container">');
            var MainHeadingComboDiv = $('<div class="d-flex justify-content-between vas-t10exp-div">');
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
                        'rgba(255, 99, 132, 0.7)',  // Red
                        'rgba(54, 162, 235, 0.7)',   // Blue
                        'rgba(255, 206, 86, 0.7)',   // Yellow
                        'rgba(75, 192, 192, 0.7)',   // Teal
                        'rgba(153, 102, 255, 0.7)',  // Purple
                        'rgba(255, 159, 64, 0.7)',   // Orange
                        'rgba(0, 187, 0, 0.7)',      // Green
                        'rgba(255, 105, 180, 0.7)',  // Pink
                        'rgba(100, 149, 237, 0.7)',  // Cornflower Blue
                        'rgba(124, 246, 0, 0.7)'     // Lime Green
                    ];

                    // Prepare the data object for the chart
                    const data = {
                        labels: labels, // Dynamic labels
                        datasets: [{
                            backgroundColor: backgroundColors,
                            data: ExpenseAmountData,
                            borderColor: 'rgba(0,0,0,0)', // Transparent border color
                            borderWidth: 0 // No border
                        }],
                    };
                    const plugin = {
                        beforeInit: function (chart) {
                            const originalFit = chart.legend.fit;
                            chart.legend.fit = function fit() {
                                originalFit.bind(chart.legend)();
                                this.height += -10;
                            }
                        }
                    };

                    // Define the chart configuration for Doughnut chart
                    const config = {
                        type: 'doughnut',
                        data: data,
                        options: {
                            responsive: true,
                            maintainAspectRatio: false, // Makes the chart responsive
                            radius: '80%', // Adjust this value to decrease the outer radius
                            plugins: {
                                legend: {
                                    display: true,
                                    position: 'right', // Positioning the legend on the right
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
                                },
                                tooltip: {
                                    enabled: true,
                                    mode: 'index',
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
                            }
                        },
                        plugins: [plugin]
                    };

                    // Create a new canvas element and append it to the root
                    const canvas = $('<canvas></canvas>');
                    canvas.attr('width', 300);  // Set width for square aspect ratio
                    canvas.attr('height', 300); // Set height for square aspect ratio
                    var polarChart = $root.find('#vas_pieChartExpense_' + widgetID);
                    polarChart.append(canvas);

                    // Initialize the chart with the new data
                    const ctx = canvas[0].getContext('2d');
                    new Chart(ctx, config);
                    $root.find('canvas').addClass('vas-t10exp-canvas')

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