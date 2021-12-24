# Twitter-Clone
The goal of this project is to implement a Twitter Clone and a client tester/simulator in F# using Actor model. The problem statement is to implement an engine that can be paired up with WebSockets to provide full functionality. The client part (send/receive tweets) and the engine (distribute tweets) were simulated in separate OS processes.
The goal of this project is to:

Implement a Twitter like engine with the following functionality:
Register account
Send tweet. Tweets can have hashtags (e.g. #COP5615isgreat) and mentions (@bestuser)
Subscribe to user's tweets
Re-tweets (so that your subscribers get an interesting tweet you got by other means)
Allow querying tweets subscribed to, tweets with specific hashtags, tweets in which the user is mentioned (my mentions)
If the user is connected, deliver the above types of tweets live (without querying)
Implement a tester/simulator to test the above
Simulate as many users as possible
Simulate periods of live connection and disconnection for users
Simulate a Zipf distribution on the number of subscribers. For accounts with a lot of subscribers, increase the number of tweets. Make some of these messages re-tweets
Measure various aspects of the simulator and report performance

• How to run your code? 
dotnet run proj4.fsproj
