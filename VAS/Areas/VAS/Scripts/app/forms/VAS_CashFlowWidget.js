/************************************************************
 * Module Name    : VAS
 * Purpose        : This Widget is used to showcase Cash Flow in system
 * chronological  : Development
 * Created Date   : 20 November 2024
 * Created by     : VIS_427
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_CashFlowWidget = function () {
        var widgetID = 0;
        this.frame;
        this.windowNo;
        this.widgetInfo;
        var $bsyDiv;
        var $self = this;
        var $maindiv = null;
        var precision = 0;
        var vDifferentYearDataList = null;
        var $root = $('<div class="h-100 w-100 vas-cashFlow-background">');

        /** Load Design on Load */
        this.initalize = function () {
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) != 0 ? this.widgetInfo.AD_UserHomeWidgetID : $self.windowNo);

            GetColumnID();

            createBusyIndicator();

            $maindiv = $('<div id="VAS-BarLine-ERP_' + widgetID + '" class="vas-barline-cashFlow-container">');
            var MainHeadingComboDiv = $('<div class="d-flex justify-content-between vas-cashFlow-heading">');
            var HeadingDiv = $('<div class= "vas-cf-heading">' + VIS.Msg.getMsg("VAS_CashFlowWidget") + '</div>');

            // Create Control Div
            $DifferentYearDataListDiv = $('<div class="input-group vis-input-wrap">');
            /* parameters are: context, windowno., coloumn id, display type, DB coloumn name, Reference key, Is parent, Validation Code*/
            $DifferentYearDataListLookUp = VIS.MLookupFactory.get(VIS.Env.getCtx(), widgetID, 0, VIS.DisplayType.List, "VAS_CashFlowWidgetList", ColumnIds.AD_Reference_ID, false, null);
            // Parameters are: columnName, mandatory, isReadOnly, isUpdateable, lookup,display length
            vDifferentYearDataList = new VIS.Controls.VComboBox("VAS_CashFlowWidgetList", true, false, true, $DifferentYearDataListLookUp, 20);
            //default value set for 6 months
            vDifferentYearDataList.setValue("05");
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
                $self.getCashFlowvDetails();
            }
        };

        /** Get data and load */
        this.getCashFlowvDetails = function () {
            // Show busy indicator while data is loading
            $bsyDiv[0].style.visibility = "visible";

            // Define your AJAX request
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetCashFlowData",
                { "ListValue": vDifferentYearDataList.getValue() }, function (dr) {
                    var CashFlowData = dr;

                    // Remove existing canvas if exists
                    $root.find('canvas').remove();

                    if (CashFlowData != null && CashFlowData.ErrorMessage != null) {
                        // If there is no data, show "No record found" message
                        $maindiv.append('<div class="vas-igwidg-notfounddiv" id="vas_norecordcont_' + widgetID + '">' + VIS.Msg.getMsg("VAS_RecordNotFound") + '</div>');
                        $root.append($maindiv);
                    } else {
                        const precision = CashFlowData.stdPrecision;
                        // Define static labels and colors
                        const lstLabel = CashFlowData.labels;
                        const zeroLinePlugin = {
                            id: 'zeroLine',
                            beforeDraw: function (chart) {
                                const ctx = chart.ctx;
                                const yScale = chart.scales.y;
                                const xScale = chart.scales.x;

                                // Find the pixel for 0 on the Y-axis
                                const zeroY = yScale.getPixelForValue(0);

                                // Draw the line
                                ctx.save();
                                ctx.beginPath();
                                ctx.moveTo(xScale.left, zeroY);
                                ctx.lineTo(xScale.right, zeroY);
                                ctx.lineWidth = 1;
                                ctx.strokeStyle = 'rgb(211,211,211)';
                                ctx.stroke();
                                ctx.restore();
                            }
                        };

                        // Prepare the data object for the chart
                        const data = {
                            labels: lstLabel, // Dynamic labels
                            datasets: [
                                {
                                    label: VIS.Msg.getMsg("VAS_CashOut"),
                                    data: CashFlowData.lstCashOutData,
                                    borderColor: 'rgba(0,0,0,0)', // Transparent border color
                                    borderWidth: 0, // No border
                                    backgroundColor: 'rgba(255, 99, 132, 0.7)',
                                    order: 1
                                },
                                {
                                    label: VIS.Msg.getMsg("VAS_CashIn"),
                                    data: CashFlowData.lstCashInData,
                                    borderColor: 'rgba(0,0,0,0)', // Transparent border color
                                    borderWidth: 0, // No border
                                    backgroundColor: 'rgba(0, 187, 0, 0.7)',
                                    order: 1
                                },
                            ],
                        };

                        // Plugin to adjust legend height (optional)
                        const plugin = {
                            beforeInit: function (chart) {
                                const originalFit = chart.legend.fit;
                                chart.legend.fit = function fit() {
                                    originalFit.bind(chart.legend)();
                                    this.height += -5; // Adjust legend height if needed
                                };
                            }
                        };

                        // Define the chart configuration for the bar chart
                        const config = {
                            type: 'bar',
                            data: data,
                            options: {
                                responsive: true,
                                maintainAspectRatio: false,
                                scales: {
                                    x: {
                                        grid: {
                                            display: true // Show grid lines on the x-axis
                                        }
                                    },
                                    y: {
                                        grid: {
                                            display: false, // Hide grid lines on the y-axis
                                        },
                                      //beginAtZero: true, // Ensure y-axis starts at 0
                                    },
                                },
                                plugins: {
                                    legend: {
                                        display: true, // Enable legend
                                        position: 'bottom',
                                        padding: {
                                            top: 0,
                                            bottom: 0
                                        },
                                    },
                                    tooltip: {
                                        enabled: true,
                                        mode: 'nearest',
                                        intersect: true,
                                        callbacks: {
                                            label: function (tooltipItem) {
                                                const dataIndex = tooltipItem.dataIndex;
                                                const datasetIndex = tooltipItem.datasetIndex;
                                                const dataset = tooltipItem.chart.data.datasets[datasetIndex];
                                                const labels = tooltipItem.chart.data.labels;
                                                const dsLabel = dataset.label;
                                                const value = dataset.data[dataIndex];
                                                return dsLabel + " - " + labels[dataIndex] + ': ' + parseFloat(value).toLocaleString(window.navigator.language, { minimumFractionDigits: precision, maximumFractionDigits: precision });
                                            }
                                        }
                                    },
                                },
                            },
                            plugins: [plugin] // Apply the plugins
                        };

                        // Create a new canvas element and append it to the root
                        const canvas = $('<canvas class="vas-expay-barline-canvas"></canvas>').css({
                            width: '100%',   // Set width to 100% (or any fixed value)
                            height: '100%'  // Set height for the canvas
                        });

                        // Append canvas to the root container
                        var polarChart = $root.find('#VAS-BarLine-ERP_' + widgetID);
                        polarChart.append(canvas);

                        // Initialize the chart with the new data
                        const ctx = canvas[0].getContext('2d');
                        const chart = new Chart(ctx, config);

                        // Ensure the canvas resizes dynamically on window resize
                        //function resizeChart() {
                        //    // Set the canvas internal dimensions based on container size
                        //    const container = polarChart;  // `polarChart` is the container
                        //    const width = container.width();
                        //    const height = container.height();

                        //    canvas[0].width = width;   // Set canvas internal width
                        //    canvas[0].height = height; // Set canvas internal height

                        //    chart.resize(); // Re-render the chart to reflect the new canvas size
                        //}

                        //// Initial resize to make sure the chart fits correctly when loaded
                        //resizeChart();

                        // Check if the chart was initialized correctly
                        if (!chart) {
                            console.error("Failed to initialize the chart.");
                        }
                    }

                    // Hide busy indicator once data is loaded
                    $bsyDiv[0].style.visibility = "hidden";
                });
        };


        /** Get List Reference ID for filter Data */
        var GetColumnID = function () {
            ColumnIds = VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetColumnID", { "refernceName": "VAS_CashFlowWidgetList" }, null);
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
            $self.getCashFlowvDetails();
        };
    };

    /**
     * Int Function for Intialize Form
     * @param {any} windowNo
     * @param {any} frame
     */
    VAS.VAS_CashFlowWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var self = this;
        window.setTimeout(function () {
            self.getCashFlowvDetails();
        }, 50);
    };

    /**
     * Widget Size change
     * @param {any} widget
     */
    VAS.VAS_CashFlowWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    /** refresh Widget Function */
    VAS.VAS_CashFlowWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    /** Dispose Function */
    VAS.VAS_CashFlowWidget.prototype.dispose = function () {
        this.frame = null;
        this.windowNo = null;
        $bsyDiv = null;
        $self = null;
        $root = null;
    };

})(VAS, jQuery);
