; (function (VIS, $) {

    var Level = VIS.Logging.Level;
    var Util = VIS.Utility.Util;

    function CalloutObjectData() {
        VIS.CalloutEngine.call(this, "VIS.CalloutObjectData"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutObjectData, VIS.CalloutEngine);//inherit CalloutEngine

    CalloutObjectData.prototype.Objectchk = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "") {
            return "";
        }
        if (value == null) {
            return "";
        }

        var FO_OBJ_DATA_ID = Util.getValueOfInt(mTab.getValue("FO_OBJECT_DATA_ID"));//Object Data ID
        //Integer acc_ID=null; //Accomodation ID
        var oldstartdate;//From Date stored in Database
        var oldtilldate;//Till Date stored in Database
        var todaydate = Util.getValueOfDateTime(mTab.getValue("TODAYDATE"));//Date 
        if (FO_OBJ_DATA_ID == null) {
            return "";
        }
        else {
            var sql = "select FO_RES_ACCOMODATION_ID,DATE_FROM,TILL_DATE from FO_RES_ACCOMODATION where FO_OBJECT_DATA_ID=@FO_OBJ_DATA_ID";
            var dr = null;
            try {
                //SqlParameter[] param = new SqlParameter[1];
                var param = [];
                param[0] = new VIS.DB.SqlParam("@FO_OBJ_DATA_ID", FO_OBJ_DATA_ID);
                dr = VIS.DB.executeReader(sql, param, null);
                //PreparedStatement pst = DataBase.prepareStatement(sql, null);
                //pst.setInt(1, FO_OBJ_DATA_ID);
                //ResultSet rs = pst.executeQuery();
                while (dr.read()) {
                    //	acc_ID= rs.getInt(1);
                    oldstartdate = Util.getValueOfDateTime(dr[1]);
                    oldtilldate = Util.getValueOfDateTime(dr[2]);
                    // curr_date is the current date holding the cursor
                    var curr_date = oldstartdate;
                    //if (todaydate.after(oldtilldate) == true)
                    if (todaydate.compareTo(oldtilldate) > 0) {
                        mTab.setValue("RES_STATUS", false);
                        return "";
                    }
                    //else if (todaydate.before(oldstartdate) == true)
                    else if (todaydate.compareTo(oldstartdate) < 0) {
                        mTab.setValue("RES_STATUS", false);
                        return "";
                    }
                    //else if (todaydate.after(oldstartdate) && todaydate.before(oldtilldate))
                    else if ((todaydate.compareTo(oldstartdate) > 0) && (todaydate.compareTo(oldtilldate) < 0)) {
                        while (!(curr_date.compareTo(oldtilldate) == 0)) {
                            if (todaydate.compareTo(curr_date) == 0) {
                                mTab.setValue("RES_STATUS", true);
                            }
                            //curr_date = TimeUtil.addDays(curr_date, 1);
                            curr_date = curr_date.AddDays(1);
                        }
                    }
                }
                dr.close();
                //pst.close();
            }
            catch (err) {
                this.setCalloutActive(false);
                if (dr != null) {
                    dr.close();
                }
                this.log.severe(e.toString());
            }
            finally {
                if (dr != null) {
                    dr.close();
                }
            }
        }
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VIS.Model.CalloutObjectData = CalloutObjectData;
    //*************CalloutObjectData End*********

    //************CalloutOffer Start********
    function CalloutOffer() {
        VIS.CalloutEngine.call(this, "VIS.CalloutOffer"); //must call
    };
    VIS.Utility.inheritPrototype(CalloutOffer, VIS.CalloutEngine);//inherit CalloutEngine
    /**
    * 
    * @param ctx context
    * @param windowNo window no
    * @param mTab tab
    * @param mField field
    * @param value value
    * @return null or error message
    */
    CalloutOffer.prototype.Datechk = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null || value.toString() == "") {
            return "";
        }
        var a = Util.getValueOfDateTime(value);
        if (a == null || a == 0) {
            return "";
        }
        var org_id = mTab.getValue("AD_ORG_ID");
        var days_Payable = 0;//Payable Days stored in settings window
        //String sql = "select  DAYSPAYABLE2 from fo_deposits where FO_DEPOSITS_ID=1000000 ";

        // Query changed because of the change of the table (Sandeep 6-10-2009)
        var sql = "select  DAYSPAYABLE2 from FO_SETTINGS where AD_ORG_ID=" + org_id;
        var dr = null;
        try {
            dr = VIS.DB.executeReader(sql, null, null);
            while (dr.read()) {
                days_Payable = Util.getValueOfInt(dr[0].toString());//.getInt(1);
                // Console.WriteLine("Days Payable"+days_Payable);
            }
            dr.close();
        }
        catch (err) {
            this.setCalloutActive(false);
            if (dr != null) {
                dr.close();
            }
            this.log.severe(err.toString());
        }

        var dt = Util.getValueOfDateTime(mTab.getValue("DATE1"));
        //Console.WriteLine("dt"+dt);
        //java.sql.Timestamp incdays;
        var incdays;
        incdays = dt.AddDays(days_Payable);//incdays = TimeUtil.AddDays(dt, days_Payable);
        //Console.WriteLine("Inc Days"+incdays);
        mTab.setValue("DATE2", incdays);

        var Offer;
        var Offer_num = 100;
        var sql1 = "select max(OFFERNO)from FO_OFFER";
        try {
            dr = VIS.DB.executeReader(sql1, null, null);
            while (dr.read()) {
                Offer_num = Util.getValueOfInt(dr[0].toString());//.getInt(1);
            }
            dr.close();
        }
        catch (err) {
            this.setCalloutActive(false);
            if (dr != null) {
                dr.close();
            }
            this.log.severe(err.toString());
        }
        finally {
            if (dr != null) {
                dr.close();
            }
        }
        if (Offer_num == null | Offer_num == 0) {
            Offer = 100;

        }
        else {
            Offer = Offer_num + 1;

        }
        mTab.setValue("OFFERNO", Offer);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    /**
    * 
    * @param ctx context
    * @param windowNo window no
    * @param mTab tab
    * @param mField field
    * @param value value
    * @return null or error message
    */
    CalloutOffer.prototype.guestpricelist = function (ctx, windowNo, mTab, mField, value, oldValue) {
        if (value == null) {
            return "";
        }
        //int add_ID = (int)value;
        var add_ID = Util.getValueOfInt(value);
        if (add_ID == null || add_ID == 0)
            return "";
        //int add_ID = (Integer)mTab.getValue("FO_ADDRESS_ID");
        var sql = "select FO_PRICE_LIST_ID from FO_ADDRESS_PRICE where FO_ADDRESS_ID=" + add_ID;
        var pricelist_ID = 0;
        var dr = null;
        try {
            //PreparedStatement pst = DataBase.prepareStatement(sql, null);

            //ResultSet rs = pst.executeQuery();
            dr = VIS.DB.executeReader(sql, null, null);
            while (dr.read()) {
                pricelist_ID = Util.getValueOfInt(dr[0]);//rs.getInt(1);
            }
            dr.close();
            // pst.close();
        }
        catch (err) {
            this.setCalloutActive(false);
            if (dr != null) {
                dr.close();
            }
            this.log.severe(err.toString());
        }
        finally {
            if (dr != null) {
                dr.close();
            }
        }
        mTab.setValue("FO_PRICE_LIST_ID", pricelist_ID);
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VIS.Model.CalloutOffer = CalloutOffer;

    function copyname() {
        VIS.CalloutEngine.call(this, "VIS.copyname");//must call
    };
    VIS.Utility.inheritPrototype(copyname, VIS.CalloutEngine); //inherit prototype
    copyname.prototype.product = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        mTab.setValue("Help", mTab.getValue("Name"));
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };

    copyname.prototype.product2 = function (ctx, windowNo, mTab, mField, value, oldValue) {
        //  
        mTab.setValue("Description", mTab.getValue("Help"));
        ctx = windowNo = mTab = mField = value = oldValue = null;
        return "";
    };
    VIS.Model.copyname = copyname;

})(VIS, jQuery);