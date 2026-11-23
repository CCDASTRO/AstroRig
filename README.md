# AstroRig

AstroRig is a C# Windows desktop app for keeping one equipment inventory and assembling multiple astrophotography rigs from it.

## Latest changes

- Use **Show rig cost** to see the selected rig's total recorded purchase cost. Assigned equipment and loaded filters each count once.
- Rig tables, payload summaries, and equipment tooltips show weights in both kilograms and pounds.
- Excel exports open on the **Equipment** sheet.

- The **Help** menu opens the AstroRig guide, Windows downloads, and N.I.N.A. plugin releases.

- Hover over any equipment name on **Rigs** to see its saved specs. Filter-wheel tooltips also show the occupied slots and filter names.

- Filter wheels store the filter assigned to each numbered slot. Hover over the wheel name on the **Rigs** tab to see the occupied slots and filter names.
- The rig role dropdown handles unrecognized saved roles without interrupting the screen. Choose a supported role to replace an unrecognized value.
- The [AstroRig plugin for N.I.N.A.](https://github.com/CCDASTRO/AstroRig.NINA) can open the same inventory and lists the filters directly in the rig table.

## Run the portable app

Extract the Windows x64 ZIP to a writable parent folder and open **AstroRig\AstroRig.exe**. To update, close the app, move any inventory you saved inside the app folder to a separate location, delete the installed AstroRig folder, and extract the new ZIP into the same parent folder. AstroRig uses the same inventory folder as earlier Imaging Rig releases. Launch AstroRig from the new folder after closing the earlier app. Keep the **runtime** folder and the other files beside the executable. The archive includes the .NET 9 desktop runtime.

On first launch, **Save** writes to **%LOCALAPPDATA%\ImagingRig\inventory.json**. Use **Save As** to write a separate inventory file in a folder you choose. **Save** then updates that file. Use **Open** to switch between inventories. The bottom status bar shows the active file path, and the app reopens your last inventory when it is available.

## Add equipment and build rigs

1. On **Equipment**, click **Add equipment**, choose the equipment type, and click **Next**. Enter the model and relevant specs in the second window, then click **Add**. A mount shows payload ratings and a camera shows sensor specs.
2. On **Rigs**, click **Add rig**, then **Assign equipment**. Select an inventory item. You can assign the same item to several rigs and maintain its specs in one place.
3. In the rig's component list, edit each role and select whether the item counts toward payload or backfocus. Use **Move up** and **Move down** to set the order. Enter binning and target backfocus above the list. Click **Save**.

Each rig uses the first component assigned to each primary role for its optical and mount calculations. Payload excludes the mount and tripod by default and reports missing component weights. Headroom uses the imaging target when supplied, or the rated mount capacity. Enter camera sensor backfocus from the sensor to its actual mounting surface, then count any separate spacers, tilt plates, or off-axis guiders in the component list. Enter a reducer's required backfocus on the reducer itself. A backfocus target entered on the rig overrides that value.

## Filter wheels

On **Equipment**, add each individual filter using the **Filter** type. Add a **Filter Wheel**, choose a preset or enter its model and number of slots, then select the wheel and click **Assign filters to slots**. You can also select the wheel in the rig table and use the same button there. Choose an inventory filter or **(Empty)** for each numbered slot, click **OK**, and click **Save**. The slot assignments follow the wheel when it is used in multiple rigs. If an existing wheel was entered as a **Filter**, change its **Equipment type** to **FilterWheel** and set its slot count. Slot assignments record the wheel layout. The rig checkboxes still control which assigned components contribute to payload and backfocus.

## Equipment presets

In the details window, select an optional preset and click **Use preset**. Review or edit the populated values, or skip the preset and type everything manually. The offline catalog contains camera and guide-camera sensor specs, telescope and guide-scope aperture and focal length, and filter wavelengths and bandpasses. Catalog values vary in coverage. Enter any missing specs for your configuration, especially camera weight and backfocus.

The camera, telescope, and filter presets are adapted from [AstroManager](https://github.com/ARP273-ROSE/astromanager), distributed under the MIT license in **AstroManager-LICENSE.txt**. Source snapshots are the camera file at blob `dd0dd9b244983dbfd6d906b17932a107aaa859f8`, the telescope file at `8a39617a1baa5cd48fb0e90197d2bff923236762`, and the filter file at `fa6237e41eb7db63fcbc274950d715b038a754f1`. Sensor dimensions are calculated from pixel size and resolution where useful.

The mount presets use manufacturer specifications for the [Paramount MX+](https://www.bisque.com/wp-content/uploads/2024/01/Feature-Comparison-Between-the-Paramount-Series-6-and-Paramount-Classic.pdf), [AM5N](https://www.zwoastro.com/wp-content/uploads/2026/03/b063feaca7a20b4c23090773c351e501.pdf), and [EQ6-R Pro](https://www.skywatcherusa.com/collections/mounts/products/eq6-r-pro). Manufacturer specs supplement the presets for the [Askar 130PHQ](https://www.sharpstar-optics.com/Products_1/78.html), [Askar 71F](https://www.sharpstar-optics.com/Products_1/101.html), and [ZWO ASI6200MC Pro](https://i.zwoastro.com/zwo-website/manuals/ASI6200_Manual_EN_v1.4.pdf).

## Import an AstroArmory backup

Click **Import AstroArmory backup**, select the SQLite **.db** file, and confirm the import. Import reads the file and adds equipment and rigs to the standalone inventory. The scope of the import is equipment, supported specifications, and rig assignments. Imaging projects and capture sessions are outside this version. Importing the same file again adds new records.

Imported numbers are copied as entered in the backup. In the supplied backup, the ASI6200MC Pro spacing is **55 mm**. Check that value against the camera's actual configuration before relying on the backfocus total.

## Export

Click **Export Excel** and choose a filename. The **.xlsx** workbook contains filterable **Rigs**, **Equipment**, and **Rig Components** sheets. Computed rig values are snapshots of the current app data. Export works without Excel installed.

## Build from source

Install the .NET 9 SDK and Windows Desktop Runtime, then run:

```powershell
dotnet build AstroRig.csproj -c Release
dotnet run --project AstroRig.csproj
```

