/************************************************************
 * Module Name    : VAS
 * Purpose        : This Widget is used to showcase the Expense Revenue And Profit Data
 * chronological  : Development
 * Created Date   : 03 October 2024
 * Created by     : VIS_0045
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_ExpRevProfitWidget = function () {
        var widgetID = 0;
        this.frame;
        this.windowNo;
        this.widgetInfo;
        var $bsyDiv;
        var $self = this;
        var $maindiv = null;
        var vDifferentYearDataList = null;
        var $root = $('<div class="h-100 w-100 vas-ExRePr-background">');

        /** Load Design on Load */
        this.initalize = function () {
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) != 0 ? this.widgetInfo.AD_UserHomeWidgetID : $self.windowNo);

            GetColumnID();

            createBusyIndicator();

            $maindiv = $('<div id="VAS-BarLine-ERP_' + widgetID + '" class="vas-barline-ExRePr-container">');
            var MainHeadingComboDiv = $('<div class="d-flex justify-content-between vas-ExRePr-heading">');
            var HeadingDiv = $('<div class= "">' + VIS.Msg.getMsg("VAS_ExpRevProfitWid") + '</div>');

            // Create Control Div
            $DifferentYearDataListDiv = $('<div class="input-group vis-input-wrap">');
            /* parameters are: context, windowno., coloumn id, display type, DB coloumn name, Reference key, Is parent, Validation Code*/
            $DifferentYearDataListLookUp = VIS.MLookupFactory.get(VIS.Env.getCtx(), widgetID, 0, VIS.DisplayType.List, "VAS_ExpRevProfitWidget", ColumnIds.AD_Reference_ID, false, null);
            // Parameters are: columnName, mandatory, isReadOnly, isUpdateable, lookup,display length
            vDifferentYearDataList = new VIS.Controls.VComboBox("VAS_ExpRevProfitWidget", true, false, true, $DifferentYearDataListLookUp, 20);
            vDifferentYearDataList.setValue("01");
            var $DifferentYearDataListControlWrap = $('<div class="vis-control-wrap">');
            $DifferentYearDataListDiv.append($DifferentYearDataListControlWrap);
            $DifferentYearDataListControlWrap.append(vDifferentYearDataList.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' '));
            $DifferentYearDataListDiv.append($DifferentYearDataListControlWrap);

            // Add Control into Maine Heading Div
            MainHeadingComboDiv.append(HeadingDiv).append($DifferentYearDataListDiv);

            // Add Maine Hedaeding dive into main div
            $maindiv.append(MainHeadingComboDiv);

            // Add maine div into Root
            $root.append($maindiv)

            // Fire Value Change Evenet for the year Control
            vDifferentYearDataList.fireValueChanged = function () {
                vDifferentYearDataList.setValue(vDifferentYearDataList.getValue());
                $bsyDiv[0].style.visibility = "visible";
                $maindiv.find('#vas_norecordcont_' + widgetID).remove();
                $self.getExpRevDetails();
            }
        };

        /** Get data and load */
        this.getExpRevDetails = function () {
            // Show busy indicator while data is loading
            $bsyDiv[0].style.visibility = "visible";

            // Define your AJAX request
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetIncomeAndExpenseData",
                { "ListValue": vDifferentYearDataList.getValue() }, function (dr) {
                    var ExpRevData = dr;

                    // Remove existing canvas if exists
                    $root.find('canvas').remove();

                    if (ExpRevData != null && ExpRevData.ErrorMessage != null) {
                        $maindiv.append('<div class="vas-igwidg-notfounddiv" id="vas_norecordcont_' + widgetID + '">' + VIS.Msg.getMsg("VAS_RecordNotFound") + '</div>')
                        $root.append($maindiv);
                    }
                    else {

                        // Define static labels and colors
                        const labels = ExpRevData.lstLabel;

                        // Prepare the data object for the chart
                        const data = {
                            labels: labels, // Dynamic labels
                            datasets: [
                                {
                                    label: VIS.Msg.getMsg("VAS_Expenses"),
                                    data: ExpRevData.lstExpData,
                                    borderColor: 'rgba(0,0,0,0)', // Transparent border color
                                    borderWidth: 0, // No border
                                    backgroundColor: 'rgb(204, 0, 0, 0.6)',
                                    order: 1
                                },
                                {
                                    label: VIS.Msg.getMsg("VAS_Revenue"),
                                    data: ExpRevData.lstRevData,
                                    borderColor: 'rgba(0,0,0,0)', // Transparent border color
                                    borderWidth: 0, // No border
                                    backgroundColor: 'rgba(0, 102, 204, 0.6)',
                                    order: 1
                                },
                                {
                                    label: VIS.Msg.getMsg("VAS_Profit"),
                                    data: ExpRevData.lstProfitData,
                                    borderColor: 'rgba(51, 102, 0, 0.5)', // Transparent border color
                                    borderWidth: 2,
                                    backgroundColor: 'rgba(51, 102, 0, 0.5)',
                                    type: 'line',
                                    tension: 0.4,
                                    fill: false, /* No fill under the line */
                                    order: 2 /*Draw this last (higher z-index) */
                                },
                            ],
                        };

                        // this is used to set the padding for legend
                        const plugin = {
                            beforeInit: function (chart) {
                                const originalFit = chart.legend.fit;
                                chart.legend.fit = function fit() {
                                    originalFit.bind(chart.legend)();
                                    this.height += -10;
                                }
                            }
                        };

                        // Define the chart configuration for BAR / Line chart
                        const config = {
                            type: 'bar',
                            data: data,
                            options: {
                                responsive: true,
                                layout: {
                                    padding: 0
                                },
                                scales: {
                                    x: {
                                        grid: {
                                            display: true // Hide the grid lines on the x-axis   
                                        }
                                    },
                                    y: {
                                        grid: {
                                            display: false, // Hide the grid lines on the y-axis
                                            //beginAtZero: true,
                                        }
                                    },
                                    y1: {
                                        /* type: 'linear',*/
                                        display: false,
                                        /*position: 'right',*/
                                        beginAtZero: false,
                                        /*grid: {
                                            drawOnChartArea: false, // only want the grid lines for one axis to show up
                                        },*/
                                    },
                                },
                                plugins: {
                                    title: {
                                        display: true,
                                        text: " ",
                                        align: 'start',
                                        /*font: {
                                          size: 18
                                        },*/
                                        padding: {
                                            top: 0,
                                            bottom: 0
                                        }
                                    },
                                    legend: {
                                        display: true,
                                        position: 'bottom', // Positioning the legend on the right
                                        padding: {
                                            top: 0,
                                            bottom: 0
                                        },
                                    },
                                    tooltip: {
                                        callbacks: {
                                            label: function (tooltipItem) {
                                                const dataIndex = tooltipItem.dataIndex;
                                                const datasetIndex = tooltipItem.datasetIndex;
                                                const dataset = tooltipItem.chart.data.datasets[datasetIndex];
                                                const labels = tooltipItem.chart.data.labels;
                                                const dsLabel = dataset.label;
                                                const value = dataset.data[dataIndex];
                                                return dsLabel + " - " + labels[dataIndex] + ': ' + value;
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
                            plugins: [plugin]
                        };

                        // Create a new canvas element and append it to the root
                        const canvas = $('<canvas class="vas-barline-ExRePr-canvas"></canvas>');
                        var polarChart = $root.find('#VAS-BarLine-ERP_' + widgetID);
                        polarChart.append(canvas);

                        // Initialize the chart with the new data
                        const ctx = canvas[0].getContext('2d');
                        new Chart(ctx, config);

                    }

                    // Hide busy indicator
                    $bsyDiv[0].style.visibility = "hidden";
                });
        };

        /** Get List Reference ID for filter Data */
        var GetColumnID = function () {
            ColumnIds = VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetColumnID", { "refernceName": "VAS_ExpRevProfitWidget" }, null);
        }

        /** Add busy Indicator in Root */
        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $bsyDiv[0].style.visibility = "visible";
            $root.append($bsyDiv);
        };

        /** Get Root */
        this.getRoot = function () {
            return $root;
        };

        /** Refresh Widget Data */
        this.refreshWidget = function () {
            $bsyDiv[0].style.visibility = "visible";
            $maindiv.find('#vas_norecordcont_' + widgetID).remove();
            $self.getExpRevDetails();
        };
    };

    /**
     * Int Function for Intialize Form
     * @param {any} windowNo
     * @param {any} frame
     */
    VAS.VAS_ExpRevProfitWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var self = this;
        window.setTimeout(function () {
            self.getExpRevDetails();
        }, 50);
    };

    /**
     * Widget Size change
     * @param {any} widget
     */
    VAS.VAS_ExpRevProfitWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    /** refresh Widget Function */
    VAS.VAS_ExpRevProfitWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    /** Dispose Function */
    VAS.VAS_ExpRevProfitWidget.prototype.dispose = function () {
        this.frame = null;
        this.windowNo = null;
        $bsyDiv = null;
        $self = null;
        $root = null;
    };

})(VAS, jQuery);