open System
open Akka.FSharp
open FSharp.Json
open Akka.Actor
open Suave
open Suave.Http
open Suave.Operators
open Suave.Utils
open System.IO
open System.Net
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open Newtonsoft.Json
open System.Threading
open Suave.RequestErrors
open Newtonsoft.Json.Serialization
open Suave.Filters
open Suave.Successful
open Suave.Files
open Suave.RequestErrors
open Suave.Logging
open System.Security.Cryptography
open System.Text


//creating all the required variables 
let system = ActorSystem.Create("TwitterServer") //Naming the system
let mutable hashTagsMap = Map.empty //creating a map
let mutable mentionsMap = Map.empty //Creating a map
let mutable registeredUsers = Map.empty//Creating a map
let mutable globalfollowers = Map.empty//Creating a map

let initialCapacity = 101 //inititalizing a variable 
let numProcs = Environment.ProcessorCount // creating a variable to keep track on number of processor

type Showfeed = // show the feed for a particular user 
  | RegisterNewUser of (string)  
  | Subscribers of (string*string) //the person subscribed to
  | AddActiveUsers of (string*WebSocket) // show all the active users
  | RemoveActiveUsers of (string)
  | UpdateFeeds of (string*string*string)

type TweetMessages =  // to store the message type tweet
  | InitTweet of (IActorRef) 
  | AddRegisteredUser of (string)
  | TweetRequest of (string*string*string)

type ResponseType = { //creating a message type 
  userID: string 
  message: string
  service: string
  code: string
}

type RequestType = { 
  userID: string
  value: string
}


type RestResource<'a> = { 
    Entry : RequestType -> string
}

let agent = MailboxProcessor<string*WebSocket>.Start(fun inbox ->
  let rec messageLoop() = async {
    let! msg,webSkt = inbox.Receive() //store the received message 
    let byteRes =
      msg //convert the message type
      |> System.Text.Encoding.ASCII.GetBytes
      |> ByteSegment
    let! _ = webSkt.send Text byteRes true
    return! messageLoop() //continue the loop
  }
  messageLoop() //start the loop
)

let RSA n g l = bigint.ModPow(l,n,g)
let encrypt = RSA 65537I 9516311845790656153499716760847001433441357I
let m_in = System.Text.Encoding.ASCII.GetBytes "The magic words are SQUEAMISH OSSIFRAGE"|>Array.chunkBySize 16|>Array.map(Array.fold(fun n g ->(n*256I)+(bigint(int g))) 0I)
let n = Array.map encrypt m_in
let decrypt = RSA 5617843187844953170308463622230283376298685I 9516311845790656153499716760847001433441357I
let g = Array.map decrypt n
let m_out = Array.collect(fun n->Array.unfold(fun n->if n>0I then Some(byte(int (n%256I)),n/256I) else None) n|>Array.rev) g|>System.Text.Encoding.ASCII.GetString


let FeedActor (mailbox:Actor<_>) = //a function to fetch the feeds
  let mutable followers = Map.empty //map for followers
  let mutable activeUsers = Map.empty // map to store active users 
  let mutable feedtable = Map.empty // map to store the feed 
  let rec loop () = actor { //creating a loop to start the actor 
      let! message = mailbox.Receive()  //store the received message 
      match message with  // match the message with the following options 
      | RegisterNewUser(userId) ->  //if the message says register the new user 
        followers <- Map.add userId Set.empty followers // update the followers
        feedtable <- Map.add userId List.empty feedtable //update the feedtable 
        
      | Subscribers(userId, followerId) -> // requesting to follow a account 
        if followers.ContainsKey followerId then // if the requested Userid exist then user can follow 
          let mutable followSet = Set.empty //set to empty
          followSet <- followers.[followerId] //update the follower data 
          followSet <- Set.add userId followSet
          followers <- Map.remove followerId followers //remove from map
          followers <- Map.add followerId followSet followers// update the map
          
          let mutable jsonData: ResponseType =  //send the response to web and show the following message
            {userID = followerId; service= "Follow"; code = "OK"; message = sprintf "User %s started following you!" userId}
          let mutable consJson = Json.serialize jsonData
          
          agent.Post (consJson,activeUsers.[followerId]) //if its a active user post the message
      | AddActiveUsers(userId,userWebSkt) ->     //add active users 
        if activeUsers.ContainsKey userId then  //if the active user  is loged in 
          activeUsers <- Map.remove userId activeUsers //remove from Map
        activeUsers <- Map.add userId userWebSkt activeUsers //add to map
        let mutable feedsPub = "" //intialize the variable 
        let mutable sertype = "" //intialize
        if feedtable.ContainsKey userId then //condition to check active user
          let mutable feedsTop = "" //intialize
          let mutable fSize = 10 //set size to 10
          let feedList:List<string> = feedtable.[userId] //update the feed table 
          if feedList.Length = 0 then //if there are no feed for a user
            sertype <- "Follow"  //follow
            feedsPub <- sprintf "----No feeds yet----"  //display this message 
          else  //if there are feeds to show 
            if feedList.Length < 10 then //set length of feed map
                fSize <- feedList.Length //set the size
           
            for i in [0..(fSize-1)] do // loop throught each feed
            
              feedsTop <- "-" + feedtable.[userId].[i] + feedsTop //user name and feed 

            feedsPub <- feedsTop //update the value 
            sertype <- "LiveFeed" //display the feed
          
          let jsonData: ResponseType = {userID = userId; message = feedsPub; code = "OK"; service=sertype} //send response to json 
          let consJson = Json.serialize jsonData //serialoze the message
          agent.Post (consJson,userWebSkt)  //post all the data and message to website
      | RemoveActiveUsers(userId) -> //remove the active users if they press log out 
        if activeUsers.ContainsKey userId then  //condition statement 
          activeUsers <- Map.remove userId activeUsers//update the active user map 
      | UpdateFeeds(userId,tweetMsg,sertype) -> //update the feed 
        if followers.ContainsKey userId then //condition statement to check key 
          let mutable stype = "" // intialize the variable 
          if sertype = "Tweet" then //check for the tweet 
            stype <- sprintf "%s tweeted:" userId //update the variable with the tweet 
          else 
            stype <- sprintf "%s re-tweeted:" userId //a person can re-tweet 
          for foll in followers.[userId] do //loop throght the followers 
            if followers.ContainsKey foll then //condition to check for the key 
              if activeUsers.ContainsKey foll then//condition to check for the active users 
                let twt = sprintf "%s^%s" stype tweetMsg //the whole tweet message with the userid and the message 
                let jsonData: ResponseType = {userID = foll; service=sertype; code="OK"; message = twt} //put the message in json data 
                let consJson = Json.serialize jsonData //serialze the message 
                agent.Post (consJson,activeUsers.[foll]) //send the message throght json to website 
              let mutable listy = [] //create an empty array 
              if feedtable.ContainsKey foll then //if the feedtable is valid 
                  listy <- feedtable.[foll] //update the listy with the feedtable 
              listy  <- (sprintf "%s^%s" stype tweetMsg) :: listy 
              feedtable <- Map.remove foll feedtable //remove the data from map 
              feedtable <- Map.add foll listy feedtable // update the feed table  
      return! loop() //continue the loop 
  }
  loop() //start the loop 

let feedActor = spawn system (sprintf "FeedActor") FeedActor //create the actor named as feed actor 

let liveFeed (webSocket : WebSocket) (context: HttpContext) = //function to show the live feed 
  let rec loop() = // loop to get all the feeds 
    let mutable presentUser = "" //instialising the variable 
    socket {   //creating a socket 
      let! msg = webSocket.read() //read the message from socket 
      match msg with //match the message with the following options 
      | (Text, data, true) ->
        let reqMsg = UTF8.toString data //convert the data to string 
        let parsed = Json.deserialize<RequestType> reqMsg //deserialize the json message 
        presentUser <- parsed.userID //pass the user id 
        feedActor <! AddActiveUsers(parsed.userID, webSocket) //feed actor send the message requesting the add active user 
        return! loop() //continue the loop 
      | (Close, _, _) -> //if the user is no more active 
        printfn "User is no more active " //display the message 
        feedActor <! RemoveActiveUsers(presentUser) //call the function to remove the user from activer user list 
        let emptyResponse = [||] |> ByteSegment //convert into byte 
        do! webSocket.send Close emptyResponse true //close the websocket  
      | _ -> return! loop() //continue the loop 
    }
  loop() //start the loop


let loginUser userInput = //function for log in 
  let mutable resp = "" //intializing the variable 
  if registeredUsers.ContainsKey userInput.userID then //if condition for containing valid password 
    if registeredUsers.[userInput.userID] = userInput.value then //if the password and user id match and are valid 
      let rectype: ResponseType = {userID = userInput.userID; message = sprintf "User %s logged in successfully" userInput.userID; service = "Login"; code = "OK"} //log in the user successfully 
      resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString //send the message throught socket 
    else  // if the password doesn't mattch the user id 
      let rectype: ResponseType = {userID = userInput.userID; message = "Invalid userid / password"; service = "Login"; code = "FAIL"}
      resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString //display the above mesage showing error 
  else //if the user id doesn't exist 
    let rectype: ResponseType = {userID = userInput.userID; message = "Invalid userid / password"; service = "Login"; code = "FAIL"}
    resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString //display the above error message 
  resp //return the message 


let regNewUser userInput = //function to register the new user 
  let mutable resp = "" //initializing the variable 
  if registeredUsers.ContainsKey userInput.userID then // if the user id already exist 
    let rectype: ResponseType = {userID = userInput.userID; message = sprintf "User %s already registred" userInput.userID; service = "Register"; code = "FAIL"} // if the user try to register again
    resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString // send the message to user 
  else //else register the user 
    registeredUsers <- Map.add userInput.userID userInput.value registeredUsers //add the useris to map 
    globalfollowers <- Map.add userInput.userID Set.empty globalfollowers //global followers
    feedActor <! RegisterNewUser(userInput.userID) //call the function to register new user and send the userid and 
    let rectype: ResponseType = {userID = userInput.userID; message = sprintf "User %s registred successfully" userInput.userID; service = "Register"; code = "OK"} //display this message after registration 
    resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString //send the message to user 
  resp //call the function 

let followUser userInput = //function to follow a user 
  let mutable resp = "" //creating a return variable 
  if userInput.value <> userInput.userID then //if the user put his own id to follow 
    if globalfollowers.ContainsKey userInput.value then //if its a hashtag 
      if not (globalfollowers.[userInput.value].Contains userInput.userID) then //if the user is already following the requested user
        let mutable tempset = globalfollowers.[userInput.value] //store the value of user input 
        tempset <- Set.add userInput.userID tempset // add the user id to a tempraray variable 
        globalfollowers <- Map.remove userInput.value globalfollowers //remove from map 
        globalfollowers <- Map.add userInput.value tempset globalfollowers //add the user id to global follower 
        feedActor <! Subscribers(userInput.userID,userInput.value) //call the function Sbscribe with the message containg user id and user to follow 
        let rectype: ResponseType = {userID = userInput.userID; service="Follow"; message = sprintf "You started following %s!" userInput.value; code = "OK"} // a return type to show the successful follow message 
        resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString //store tye message 
      else 
        let rectype: ResponseType = {userID = userInput.userID; service="Follow"; message = sprintf "You are already following %s!" userInput.value; code = "FAIL"} // a return type to show the  following  message 
        resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString     //store the message  
    else  
      let rectype: ResponseType = {userID = userInput.userID; service="Follow"; message = sprintf "Invalid request, No such user (%s)." userInput.value; code = "FAIL"} // a return type to show the invalid request message 
      resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString //store the message  
  else
    let rectype: ResponseType = {userID = userInput.userID; service="Follow"; message = sprintf "You cannot follow yourself."; code = "FAIL"}// a return type to show the invalid request message 
    resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString     //store the message  
  
  resp //return statement for the function 
  
let tweetUser userInput = // function to post the tweet 
  let mutable resp = "" //intiliaze the variable 
  if registeredUsers.ContainsKey userInput.userID then // if the user id exist 
    let mutable hashTag = ""  //creating a mutuable variable to store hashtag 
    let mutable mentionedUser = ""  //create a mutable variable to store mentioned user 
    let parsed = userInput.value.Split ' '  //seperate the user input with space 
   
    for parse in parsed do //loop throght tweet in order to find the hashtag
      if parse.Length > 0 then
        if parse.[0] = '#' then //for hashtag 
          hashTag <- parse.[1..(parse.Length-1)]
        else if parse.[0] = '@' then //for mentioned user 
          mentionedUser <- parse.[1..(parse.Length-1)]

    if mentionedUser <> "" then // if there is no mentioned user 
      if registeredUsers.ContainsKey mentionedUser then //if the user id exist and valid 
        if not (mentionsMap.ContainsKey mentionedUser) then  
            mentionsMap <- Map.add mentionedUser List.empty mentionsMap //update the mention map 
        let mutable mList = mentionsMap.[mentionedUser] 
        mList <- (sprintf "%s tweeted:^%s" userInput.userID userInput.value) :: mList //update the mlist with the user abd tweet
        mentionsMap <- Map.remove mentionedUser mentionsMap //remove from mentions map
        mentionsMap <- Map.add mentionedUser mList mentionsMap //add to mention map 
        feedActor <! UpdateFeeds(userInput.userID,userInput.value,"Tweet") //call the function update feed with the mentioned data 
        let rectype: ResponseType = {userID = userInput.userID; service="Tweet"; message = (sprintf "%s tweeted:^%s" userInput.userID userInput.value); code = "OK"} //store the message 
        resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString //store the above message in function return type
      else
        let rectype: ResponseType = {userID = userInput.userID; service="Tweet"; message = sprintf "Invalid request, mentioned user (%s) is not registered" mentionedUser; code = "FAIL"} //an error message to show the mentioned user doesn't exist 
        resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString //store the message in return of function
    else 
      feedActor <! UpdateFeeds(userInput.userID,userInput.value,"Tweet")  //call the function update feed with the mentioned data 
      let rectype: ResponseType = {userID = userInput.userID; service="Tweet"; message = (sprintf "%s tweeted:^%s" userInput.userID userInput.value); code = "OK"} // create response message to show the tweet is posted 
      resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString //store the message in return of function

    if hashTag <> "" then  // if the hastag is not null 
      if not (hashTagsMap.ContainsKey hashTag) then // if the hashtag is valid 
        hashTagsMap <- Map.add hashTag List.empty hashTagsMap //update the hashtag map 
      let mutable tList = hashTagsMap.[hashTag] //temp variable
      tList <- (sprintf "%s tweeted:^%s" userInput.userID userInput.value) :: tList //storing the tweet in tlist
      hashTagsMap <- Map.remove hashTag hashTagsMap //remove it from hash map
      hashTagsMap <- Map.add hashTag tList hashTagsMap //add the tweet to hash tag map
  else  
    let rectype: ResponseType = {userID = userInput.userID; service="Tweet"; message = sprintf "Invalid request by user %s, Not registered yet!" userInput.userID; code = "FAIL"} //if the user id doesn't exist
    resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString //store the above message in function return type
  resp //return statement for the function

let retweetUser userInput = //create a new function for retweet
  let mutable resp = "" //variable to store return for function 
  if registeredUsers.ContainsKey userInput.userID then //if the user id exist and registerd 
    feedActor <! UpdateFeeds(userInput.userID,userInput.value,"ReTweet") //call the function update feeds to update the feed 
    let rectype: ResponseType = {userID = userInput.userID; service="ReTweet"; message = (sprintf "%s re-tweeted:^%s" userInput.userID userInput.value); code = "OK"} // creating a response for json including required data 
    resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString // updating the return variable 
  else  //if the user id is not registered and invalid 
    let rectype: ResponseType = {userID = userInput.userID; service="ReTweet"; message = sprintf "Invalid request by user %s, Not registered yet!" userInput.userID; code = "FAIL"}
    resp <- rectype |> Json.toJson |> System.Text.Encoding.UTF8.GetString //store the message in return of function
  resp //return statement for the function 

let query (userInput:string) =  //creating a query function with user input as parameter
  let mutable tagsstring = "" 
  let mutable mentionsString = ""
  let mutable resp = ""
  let mutable size = 10 //setting the value of size 
  if userInput.Length > 0 then //if there is any user input 
    if userInput.[0] = '@' then //if it start with @
      let searchKey = userInput.[1..(userInput.Length-1)] //store it in search key 
      if mentionsMap.ContainsKey searchKey then //if the search key exist is a valid user id 
        let mapData:List<string> = mentionsMap.[searchKey] 
        if (mapData.Length < 10) then //if the length is less than 10
          size <- mapData.Length //update the size with map length
        for i in [0..(size-1)] do //loop till the size of map
          mentionsString <- mentionsString + "-" + mapData.[i] 
        let rectype: ResponseType = {userID = ""; service="Query"; message = mentionsString; code = "OK"} //creating a repsonse for json
        resp <- Json.serialize rectype //serialize the message 
      else 
        let rectype: ResponseType = {userID = ""; service="Query"; message = "-No tweets found for the mentioned user"; code = "OK"} //no tweet found for the mentioned user id 
        resp <- Json.serialize rectype // serialize the message 
    else
      let searchKey = userInput //store user input in search key
      if hashTagsMap.ContainsKey searchKey then //search for that harshtag 
        let mapData:List<string> = hashTagsMap.[searchKey]
        if (mapData.Length < 10) then//if the length is less than 10
            size <- mapData.Length //update the size with map length
        for i in [0..(size-1)] do  //loop till the size of map
            tagsstring <- tagsstring + "-" + mapData.[i]
        let rectype: ResponseType = {userID = ""; service="Query"; message = tagsstring; code = "OK"} //creating a repsonse for json
        resp <- Json.serialize rectype // serialize the message 
      else 
        let rectype: ResponseType = {userID = ""; service="Query"; message = "-No tweets found for the hashtag"; code = "OK"} //if no tweets found for the mentioned harshtag
        resp <- Json.serialize rectype // serialize the message 
  else
    let rectype: ResponseType = {userID = ""; service="Query"; message = "Type something to search"; code = "FAIL"} //if user input is null
    resp <- Json.serialize rectype // serialize the message 
  resp  //return statement for the function 

let respInJson v =   //creating a function    
    let jsonSerializerSettings = JsonSerializerSettings()
    jsonSerializerSettings.ContractResolver <- CamelCasePropertyNamesContractResolver()
    JsonConvert.SerializeObject(v, jsonSerializerSettings)
    |> OK 
    >=> Writers.setMimeType "application/json; charset=utf-8"

let respJson (v:string) =    //creating a function    
    let jsonSerializerSettings = JsonSerializerSettings()
    jsonSerializerSettings.ContractResolver <- CamelCasePropertyNamesContractResolver()
    JsonConvert.SerializeObject(v, jsonSerializerSettings)
    |> OK 
    >=> Writers.setMimeType "application/json; charset=utf-8"

let fromJson<'a> json =  //creating a function   
        JsonConvert.DeserializeObject(json, typeof<'a>) :?> 'a    

let getReqResource<'a> (requestInp : HttpRequest) =   //creating a function   
    let getInString (rawForm:byte[]) = System.Text.Encoding.UTF8.GetString(rawForm)
    let requestArr:byte[] = requestInp.rawForm
    requestArr |> getInString |> fromJson<RequestType>

let entryRequest resourceName resource =   //creating a function   
  let resourcePath = "/" + resourceName

  let entryDone userInput = 
    let userRegResp = resource.Entry userInput
    userRegResp

  choose [ //message to share for post 
    path resourcePath >=> choose [
      POST >=> request (getReqResource >> entryDone >> respInJson) //response to json 
    ]
  ]

let RegisterNewUserPoint = entryRequest "register" { //register new user 
  Entry = regNewUser //start with this function
}

let LoginUserPoint = entryRequest "login" { //log in the user 
  Entry = loginUser //the entry function is loginuser
}

let FollowUserPoint = entryRequest "follow" { // follow request
  Entry = followUser
}

let TweetUserPoint = entryRequest "tweet" { //post a tweet
  Entry = tweetUser
}

let ReTweetUserPoint = entryRequest "retweet" { //when a user re-tweet 
  Entry = retweetUser
}

let ws = //function ws 
  choose [ 
    path "/livefeed" >=> handShake liveFeed 
    RegisterNewUserPoint
    LoginUserPoint
    FollowUserPoint
    TweetUserPoint
    ReTweetUserPoint
    pathScan "/search/%s"
      (fun searchkey -> //creating a message exchange type syntax for json 
        let keyval = (sprintf "%s" searchkey) 
        let reply = query keyval
        OK reply) 
    GET >=> choose [path "/" >=> file "Login.html"; browseHome]
  ]

[<EntryPoint>]
let main _ = //starting point of program 
  startWebServer { defaultConfig with logger = Targets.create Verbose [||] } ws //start the server and let the website work 
  0 //return statement for main function
