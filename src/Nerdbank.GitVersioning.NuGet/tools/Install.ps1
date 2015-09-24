param($installPath, $toolsPath, $package, $project)

$projectDirectory = Split-Path $project.FileName
$generatedFile = .(Join-Path $toolsPath Create-VersionTxt.ps1) -ProjectDirectory $projectDirectory

if ($generatedFile)
{
    # Add version.txt to the project so it shows up in VS.
    $result = $project.ProjectItems.AddFromFile($generatedFile)

    # By default, items get added with ItemType=Content, which could cause version.txt to be included in the build's output.
    # Set the type to None so that it is ignored.
    $result.Properties["ItemType"].Value = "None"

    $project.Save()
}
