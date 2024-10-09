/************************************************************
 * Module Name    : VAS
 * Purpose        :Get the details of PO and Create GRN
 * chronological  : Development
 * Created Date   : 20 Sep 2024
 * Created by     : VAI050
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_CreditUtilizationWidget = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;
        var $bsyDiv;
        var $self = this;
        var $root = $('<div class="h-100 w-100">'); // Root container
        this.currentPage = 1;
        this.totalPages = 0;
        var widgetID = 0, referenceID = 0;
        var pageSize = 4;
        var $divCustContainer, $divChartContainer, divCustomer, divChart, spnPageCount;
        var lookupCredit, ctrlCredit, $cmbCredit, btnUpdate;
        var bp_ID, loc_ID, creditsetting, creditVal, creditlimit, creditused;

        this.initalize = function () {
            widgetID = this.windowNo;
            createBusyIndicator();
            $root.append('<div class="VAS-creditLimit-container">' +
                '<div class= "VAS-LeftTabs-container" id="VAS_CustomerContainer_' + widgetID + '">' +
                '<div class="VAS-tab-heading">' + VIS.Msg.getMsg("VAS_CreditUtilization") + '</div>' +
                '<div class="nav" id="VAS_divCustomers_' + widgetID + '" role="tablist" aria-orientation="vertical"> ' +
                '</div>' +
                '<div class="VAS-tiles-pagination">' +
                '<i class="fa fa-arrow-circle-left VAS-page-prev" id="VAS_Prev_' + widgetID + '" aria-hidden="true"></i>' +
                '<span class="VAS-total-count" id="VAS_spnPageCount_' + widgetID + '">0 of 0</span>' +
                '<i class="fa fa-arrow-circle-right VAS-page-next" id="VAS_Next_' + widgetID + '" aria-hidden="true"></i>' +
                '</div>' +
                '</div>' +
                '<div class="VAS-creditLimit-chart" id="VAS_ChartContainer_' + widgetID + '" aria-controls="credit-limit">' +
                '<div class="VAS-chart-container" id="VAS_divChart_' + widgetID + '"></div>' +
                '<div class="VAS-inputWbtn">' +
                '<span class="VAS-info-span" style="display:none;" id="VAS_spnErrorMessage_' + widgetID + '"></span>' +
                '<div class="input-group vis-input-wrap">' +
                '<div class="vis-control-wrap" id="VAS_cmbCredit_' + widgetID + '">' +
                '<label for="VAS_Phase">' + VIS.Msg.getMsg("VAS_CreditValidation") + '</label>' +
                '</div>' +
                '</div>' +
                '<input type="button" id="VAS_btnUpdate_' + widgetID + '" class="btn ui-button ui-widget" value="Update">' +
                '</div>' +
                '</div>' +
                '</div>');

            $divCustContainer = $root.find("#VAS_CustomerContainer_" + widgetID);
            $divChartContainer = $root.find("#VAS_ChartContainer_" + widgetID);
            divCustomer = $divCustContainer.find("#VAS_divCustomers_" + widgetID);

            divChart = $divChartContainer.find("#VAS_divChart_" + widgetID);
            $cmbCredit = $divChartContainer.find("#VAS_cmbCredit_" + widgetID);
            btnUpdate = $divChartContainer.find("#VAS_btnUpdate_" + widgetID);
            spnPageCount = $root.find("#VAS_spnPageCount_" + widgetID);

            referenceID = VIS.dataContext.getJSONData(VIS.Application.contextUrl + "Common/GetReference", { Name: "CreditValidation" });
            lookupCredit = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.List, "CreditValidation", referenceID, false, "AD_Ref_List.IsActive = 'Y'");
            ctrlCredit = new VIS.Controls.VComboBox("CreditValidation", true, false, true, lookupCredit, 50);
            $cmbCredit.prepend(ctrlCredit.getControl());

            // Add event listeners for arrows
            $divCustContainer.find('#VAS_Prev_' + widgetID).on('click', function (e) {
                if ($self.currentPage > 1) {
                    $bsyDiv[0].style.visibility = "visible";
                    $self.currentPage--;
                    $self.intialLoad($self.currentPage);
                }
            });

            $divCustContainer.find('#VAS_Next_' + widgetID).on('click', function (e) {
                if ($self.currentPage < $self.totalPages) {
                    $bsyDiv[0].style.visibility = "visible";
                    $self.currentPage++;
                    $self.intialLoad($self.currentPage);
                }
            });

            btnUpdate.on('click', function () {
                if (ctrlCredit.getValue() == creditVal) {
                    return;
                }
                $bsyDiv.css('visibility', 'visible');
                VIS.dataContext.getJSONData(VIS.Application.contextUrl + "Product/UpdateCreditValidation",
                    { BP_ID: bp_ID, Loc_ID: loc_ID, CreditSetting: creditsetting, CreditValidation: ctrlCredit.getValue() }, function (result) {
                        var spnWO = $root.find('#VAS_spnErrorMessage_' + widgetID);
                        var message = "";
                        if (result == 1) {
                            creditVal = ctrlCredit.getValue();
                            message = VIS.Msg.getMsg("VAS_CreditValUpd");
                        }
                        else {
                            ctrlCredit.setValue(creditVal);
                            message = VIS.Msg.getMsg("VAS_CreditValNotUpd");
                        }
                        spnWO.text(message);
                        spnWO.fadeIn();
                        spnWO.fadeOut(5000);
                        $self.intialLoad($self.currentPage);
                    });
            });
        };

        /* This function will load data in widget */
        this.intialLoad = function (pageNo) {
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "Product/GetCustomerCredit",
                { pageNo: pageNo, pageSize: pageSize }, function (response) {
                    divCustomer.empty();
                    var statusClass = "";
                    if (response != null && response.Customers != null && response.Customers.length > 0) {
                        for (i = 0; i < response.Customers.length; i++) {

                            if (response.Customers[i]["SOCreditStatus"] == "S") {
                                statusClass = "VAS-redStatusColor";
                            }
                            else if (response.Customers[i]["SOCreditStatus"] == "H") {
                                statusClass = "VAS-orangeStatusColor";
                            }
                            else if (response.Customers[i]["SOCreditStatus"] == "O") {
                                statusClass = "VAS-greenStatusColor";
                            }
                            else if (response.Customers[i]["SOCreditStatus"] == "W") {
                                statusClass = "VAS-yellowStatusColor";
                            }
                            else {
                                statusClass = "VAS-greyStatusColor";
                            }

                            var hue = Math.floor(Math.random() * (360 - 0)) + 0;
                            var v = Math.floor(Math.random() * 16) + 80;
                            var pastel = 'hsl(' + hue + ', 100%,' + v + '%)';
                            var pastelClr = 'hsl(' + hue + ', 100%,' + (v - 50) + '%)';

                            var highlightChar = response.Customers[i].Name;
                            var txt = highlightChar.trim().split(' ');
                            highlightChar = txt[0].substring(0, 1).toUpper();
                            if (txt.length > 1) {
                                highlightChar += txt[txt.length - 1].substring(0, 1).toUpper();
                            }
                            else {
                                highlightChar = txt[0].substring(0, 2).toUpper();
                            }

                            divCustomer.append('<div class="VAS-credit-limit-nav ' + (i == 0 ? "active" : "") + '" aria-controls="credit-limit" data-creditlimit="' + response.Customers[i]["SO_CreditLimit"] +
                                '" data-creditused="' + response.Customers[i]["SO_CreditUsed"] + '" data-bpid="' + response.Customers[i]["C_BPartner_ID"] +
                                '" data-locid="' + response.Customers[i]["Location_ID"] + '" data-creditsetting="' + response.Customers[i]["CreditSetting"] +
                                '" data-creditval="' + response.Customers[i]["CreditValidation"] + '">' +
                                '<div class="VAS-credit-des">' +
                                //'<img src="' + (response.Customers[i].ImageUrl != "" ? VIS.Application.contextUrl + response.Customers[i].ImageUrl :
                                //    VIS.Application.contextUrl + 'Areas/VAS/Content/Images/dummy.jpg') + '" alt=""> ' +
                                '<div class="VAS-credit-img" style="background-color:' + pastel + '; color:' + pastelClr + '">' +
                                '<span>' + highlightChar + '</span></div>' +
                                '<div class="VAS-userCredit-detail">' +
                                '<div class="VAS-userCredit-name">' + response.Customers[i].Name + '</div>' +
                                '<div class="VAS-userCreditStatus">' + VIS.Msg.getMsg("VAS_Status") + '<span class="' + statusClass + '">' +
                                response.Customers[i]["CreditStatus"] + '</span></div>' +
                                '</div>' +
                                '</div>' +
                                '<div class="progress">' +
                                '<div class="progress-bar bg-danger" role="progressbar" style="width:' + response.Customers[i].CreditUtil + '%" aria-valuenow="100" aria-valuemin="0" aria-valuemax="100"></div>' +
                                '</div>' +
                                '</div>');

                            if (i == 0) {
                                bp_ID = response.Customers[i]["C_BPartner_ID"];
                                loc_ID = response.Customers[i]["Location_ID"];
                                creditsetting = response.Customers[i]["CreditSetting"];
                                creditVal = response.Customers[i]["CreditValidation"];
                                creditlimit = response.Customers[i]["SO_CreditLimit"];
                                creditused = response.Customers[i]["SO_CreditUsed"];
                                displayUtilizationDetails(bp_ID, loc_ID, creditsetting, creditVal, creditlimit);
                            }
                        }

                        /* Add Pagination div on first tym data load*/
                        if (pageNo == 1) {
                            $self.totalPages = Math.ceil(response.RecordCount / pageSize);
                        }
                        spnPageCount.text($self.currentPage + VIS.Msg.getMsg("VAS_Of") + $self.totalPages);

                        // Attach click event listener to delivery boxes
                        divCustomer.off('click', '.VAS-credit-limit-nav');
                        divCustomer.on('click', '.VAS-credit-limit-nav', function () {
                            $bsyDiv.css('visibility', 'visible');

                            divCustomer.find('.VAS-credit-limit-nav').removeClass('active');
                            $(this).addClass('active');

                            bp_ID = $(this).data('bpid');
                            loc_ID = $(this).data('locid');
                            creditsetting = $(this).data('creditsetting');
                            creditVal = $(this).data('creditval');
                            creditlimit = $(this).data('creditlimit');
                            creditused = $(this).data('creditused');
                            displayUtilizationDetails(bp_ID, loc_ID, creditsetting, creditVal, creditlimit);
                        });
                    }
                    $bsyDiv.css('visibility', 'hidden');
                });
        };

        function displayUtilizationDetails() {
            divChart.empty();

            // Remove existing canvas if exists
            $divChartContainer.find('canvas').remove();

            // Prepare the data object for the chart
            const data = {
                labels: [
                    VIS.Msg.getMsg("CreditLimit"),
                    VIS.Msg.getMsg("VAS_CreditUsed")
                ],
                label: VIS.Msg.getMsg("VAS_CreditUtilization"),
                datasets: [
                    {
                        backgroundColor: [
                            'rgba(255, 99, 132, 0.6)',
                            'rgba(54, 162, 235, 0.5)'
                        ],
                        data: [creditlimit, creditused],
                        borderColor: 'rgba(0,0,0,0)', // Transparent border color
                        borderWidth: 0, // No border
                        hoverOffset: 4
                    },
                ],
            };

            const plugin = {
                beforeInit: function (chart) {
                    const originalFit = chart.legend.fit;
                    chart.legend.fit = function fit() {
                        originalFit.bind(chart.legend)();
                        this.height += 10;
                    }
                }
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
            };

            // Create a new canvas element and append it to the root
            const canvas = $('<canvas></canvas>');
            divChart.append(canvas);
            canvas.css('height', '100%');
            canvas.css('width', '100%');

            // Initialize the chart with the new data
            const ctx = canvas[0].getContext('2d');
            new Chart(ctx, config);

            ctrlCredit.setValue(creditVal);
            $bsyDiv.css('visibility', 'hidden');
        }

        /* This function is used to create the busy indicator */
        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($bsyDiv);
        }

        this.getRoot = function () {
            return $root;
        };

        /* This function is used to refresh the widget data */
        this.refreshWidget = function () {
            $bsyDiv[0].style.visibility = "visible";
            $self.currentPage = 1;
            $self.totalPages = 0;
            $self.intialLoad($self.currentPage);
        };
    };

    VAS.VAS_CreditUtilizationWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener)
            this.listener.widgetFirevalueChanged(value);
    };

    VAS.VAS_CreditUtilizationWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_CreditUtilizationWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var ssef = this;
        window.setTimeout(function () {
            ssef.intialLoad(1);
        }, 50);
    };

    VAS.VAS_CreditUtilizationWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_CreditUtilizationWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_CreditUtilizationWidget.prototype.dispose = function () {
        this.frame = null;
        this.windowNo = null;
        $bsyDiv = null;
        $self = null;
        $root = null;
        $contentContainer = null;
    };

})(VAS, jQuery);