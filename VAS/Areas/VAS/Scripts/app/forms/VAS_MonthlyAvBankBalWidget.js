/************************************************************
 * Module Name    : VAS
 * Purpose        : This Widget is used to showcase the Monthly Average Balance data
 * chronological  : Development
 * Created Date   : 25 Decemeber 2024
 * Created by     : VIS_427
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_MonthlyAvBankBal = function () {
        var widgetID = 0;
        this.frame;
        this.windowNo;
        this.widgetInfo;
        var $bsyDiv;
        var $self = this;
        var $maindiv = null;
        var $root = $('<div class="h-100 w-100 vas-monAvBbal-background">');

        /** Load Design on Load */
        this.initalize = function () {
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) != 0 ? this.widgetInfo.AD_UserHomeWidgetID : $self.windowNo);

            createBusyIndicator();

            $maindiv = $('<div id="VAS-BarLine-MABB_' + widgetID + '" class="vas-barline-monAvBbal-container">');
            var MainHeadingComboDiv = $('<div class="d-flex justify-content-between vas-monAvBbal-heading">');
            var HeadingDiv = $('<div class= "">' + VIS.Msg.getMsg("VAS_MonthlyAvBalWidget") + '</div>');

            // Add Control into Maine Heading Div
            MainHeadingComboDiv.append(HeadingDiv);

            // Add Maine Hedaeding dive into main div
            $maindiv.append(MainHeadingComboDiv);

            // Add maine div into Root
            $root.append($maindiv)

        };

        /** Get data and load */
        this.getMonthlyAvBankBalDetails = function () {
            // Show busy indicator while data is loading
            $bsyDiv[0].style.visibility = "visible";

            // Define your AJAX request
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetMonthlyAvBankBalData",{ "":"" }, function (dr) {
                    var MontlyAvBalData = dr;

                    // Remove existing canvas if exists
                    $root.find('canvas').remove();

                    if (MontlyAvBalData != null && MontlyAvBalData.ErrorMessage != null) {
                        $maindiv.append('<div class="vas-igwidg-notfounddiv" id="vas_norecordcont_' + widgetID + '">' + VIS.Msg.getMsg("VAS_RecordNotFound") + '</div>')
                        $root.append($maindiv);
                    }
                    else {
                        //getting precision
                        var precision = MontlyAvBalData.stdPrecision;

                        // Define static labels
                        const labels = MontlyAvBalData.labels;

                        // Prepare the data object for the chart
                        const data = {
                            labels: labels, // Dynamic labels
                            datasets: [
                                {
                                    label: VIS.Msg.getMsg("VAS_Payments"),
                                    data: MontlyAvBalData.APPayAmt,
                                    borderColor: 'rgba(0,0,0,0)', // Transparent border color
                                    borderWidth: 0, // No border
                                    backgroundColor: 'rgb(0, 132, 196)',
                                    order: 1
                                },
                                {
                                    label: VIS.Msg.getMsg("VAS_Receipts"),
                                    data: MontlyAvBalData.ARPayAmt,
                                    borderColor: 'rgba(0,0,0,0)', // Transparent border color
                                    borderWidth: 0, // No border
                                    backgroundColor: 'rgba(255,124,128)',
                                    order: 1
                                },
                                {
                                    label: VIS.Msg.getMsg("VAS_Balances"),
                                    data: MontlyAvBalData.EndingBal,
                                    borderColor: 'rgba(86,108,176)', // Transparent border color
                                    borderWidth: 2,
                                    backgroundColor: 'rgba(86,108,176)',
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
                                                return dsLabel + " - " + labels[dataIndex] + ': ' + value.toLocaleString(window.navigator.language, { minimumFractionDigits: precision, maximumFractionDigits: precision });
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
                        const canvas = $('<canvas class="vas-barline-monAvBbal-canvas"></canvas>');
                        var polarChart = $root.find('#VAS-BarLine-MABB_' + widgetID);
                        polarChart.append(canvas);

                        // Initialize the chart with the new data
                        const ctx = canvas[0].getContext('2d');
                        new Chart(ctx, config);

                    }

                    // Hide busy indicator
                    $bsyDiv[0].style.visibility = "hidden";
                });
        };

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
            $self.getMonthlyAvBankBalDetails();
        };
    };

    /**
     * Int Function for Intialize Form
     * @param {any} windowNo
     * @param {any} frame
     */
    VAS.VAS_MonthlyAvBankBal.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var self = this;
        window.setTimeout(function () {
            self.getMonthlyAvBankBalDetails();
        }, 50);
    };

    /**
     * Widget Size change
     * @param {any} widget
     */
    VAS.VAS_MonthlyAvBankBal.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    /** refresh Widget Function */
    VAS.VAS_MonthlyAvBankBal.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    /** Dispose Function */
    VAS.VAS_MonthlyAvBankBal.prototype.dispose = function () {
        this.frame = null;
        this.windowNo = null;
        $bsyDiv = null;
        $self = null;
        $root = null;
    };

})(VAS, jQuery);