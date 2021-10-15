#!/bin/bash

if [ -z "$1" ]
then
    echo "Please provide a directory"
else
    mkdir $1
    svn add $1
    svn propset svn:ignore -F .svnignore $1
fi
