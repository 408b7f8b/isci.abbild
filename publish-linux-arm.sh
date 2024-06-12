#!/bin/bash

directory="./tmp"

# Check if the directory exists

if [ -d "$directory" ]; then    
    echo "Directory $directory exists. Deleting it now..."
    rm -rf "$directory"
    echo "Directory $directory deleted successfully."
else
    echo "Directory $directory does not exist."
fi

dotnet publish -c release --runtime linux-arm -o "$directory"