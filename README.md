# Buildpack Staging Simualtor
Simulates container staging by applying buildpacks to a target artifact. Useful for local development to see what the droplet would look like after all buildpacks are applied, especially if added as a post-build step.

### How to use
Run Build script with the following parameters:

`--buildpacks` - space seperated list of buildpacks, either URLs, local folders, or local zip files

`--push-directory` - location of folder that would be used as the source app being staged

Ex. 
````
> ./build.ps1 --buildpacks http://url1 http://url2 --push-directory c:\myproject\bin\debug
````
