                  Steps to Configure VA Standard ERP CRM code from GitHub account 
 
1.	Select  Official-VAStandard-ERP-CRM Repository, it will open as below 

  ![image](https://github.com/user-attachments/assets/6b02e9b9-17b9-4867-ac21-8bffe35d22e8)


2.	From the "Code" button, indicated by the red border in the image above, you can either clone the repository or download the code to your local machine to set up the application.
   


4.	For the database, we assumed you already setup the database and hosting files if not then please refer to the below link:
  [Hosting files and database 5.x](https://sourceforge.net/projects/erp-crm-advant/files/VIENNA%20Advantage%20HTML5%20Version/VIENNA%20Advantage%20ERP/)


5.	After download the code, Open the application in Visual Studio 2019 or latest version and go to the ViennaAdvantageWeb folder in the project shown as below.
 
  ![image](https://github.com/user-attachments/assets/eda06370-c176-45ac-9e3e-83a376815484)


5.	Copy the mentioned below Areas and their corresponding DLL files from the FTP into the respective Areas and DLL folders (Create DLL and Areas folders under ViennaAdvantageWeb folder if it doesn't exist). The FTP details are as follows:

      SFTP: viennaftp.viennaadvantage.com
  	
      User: Vienna
  	
      Port: 22
  	
      Password: dr^1#xZ06-


7.	Go to the path /Required Files for Partner-kit Setup/5.x and copy all DLLs from “CommonDlls” folder and paste them into the “DLL” folder within ViennaAdvantageWeb. As shown in below pic.

 ![image](https://github.com/user-attachments/assets/3b840123-79d3-423d-abd8-4dd7216ded9d)


7.	Navigate to /Required Files for Partner-kit Setup/5.x/ViennaBase in FTP and select the version that matches the Vienna Advantage Base Files module version in database that you setup in step 3.  

    To find the module version, search the Module Management screen from Menu after login with Role: “System Administration”. As shown in below pic.
    ![image](https://github.com/user-attachments/assets/4bdc75e2-a667-43f2-9b20-3d65c370395f)
  	

    On select, System will open Module Management screen as shown below and Search for module “Vienna Advantage Base Files” to check respective version.
  	
    ![image](https://github.com/user-attachments/assets/66781d89-b423-4dcf-a564-d25aaedb07b5)

9.	Now, go to path /Required Files for Partner-kit Setup/5.x/ViennaBase.
    Under a particular version it will show two folder Htmlbin and Area as show in below screen. Copy the DLLs from the Htmlbin folder and paste them into the DLL folder in ViennaAdvantageWeb. Similarly, copy the folders from the Areas folder and paste them into the Areas folder in ViennaAdvantageWeb. As shown in below pics.

•	Copy dll from Htmlbin folder
 ![image](https://github.com/user-attachments/assets/4229cffa-80e6-4e95-9155-1c1647acdf21)

•	Copy Area folder files
 ![image](https://github.com/user-attachments/assets/1950008d-c5f6-4cfc-809b-589330b9722e)


9.	Navigate to folder “/Required Files for Partner-kit Setup/5.x/VIS” in FTP, and select the version based on the Vienna Advantage Framework module version in your database.
    
    To find the module version, follow Step - 7.

    Under the selected version there are two folder Htmlbin and Area. Copy the DLLs from the Htmlbin folder and paste them into the DLL folder in ViennaAdvantageWeb. Likewise, copy the files from the Areas folder and paste them into the Areas folder in ViennaAdvantageWeb. As shown in below pic.


  •	Copy dll from Htmlbin folder

   ![image](https://github.com/user-attachments/assets/8ff74ebb-4a66-4ea1-9764-127cee4869d5)


  •	Copy Area folder files

   ![image](https://github.com/user-attachments/assets/99aafc89-31e5-4b38-bf9a-eb389798e5a6)



9.	Open the webconfig file located in project ViennaAdvantageWeb and Update the database connection string in the Webconfig file under Tag AppSetings against key (oracleConnectionString or postgresqlConnectionString) as per your database in order to connect to your database.
    
  ![image](https://github.com/user-attachments/assets/ea661acd-908f-4a71-a5fd-fe49e1f3faa5)


 
